using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace BEngine.Codex.Editor;

public sealed class CodexAppServerClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CodexProjectStore _store;
    private readonly CodexSessionDocument _session;
    private readonly List<CodexApprovalRequest> _approvals = [];
    private Process? _process;
    private StreamWriter? _writer;
    private Task? _startTask;
    private long _nextRequestId;
    private long _revision;
    private string _statusText = "Stopped";
    private string _activeTurnId = string.Empty;
    private string _authMode = string.Empty;
    private string _planType = string.Empty;
    private string _loginUrl = string.Empty;
    private CodexConnectionStatus _connectionStatus;
    private bool _disposed;

    public event Action? changed;

    public CodexProjectSettingsDocument Settings { get; }
    public string ProjectRoot => _store.ProjectRoot;

    public CodexAppServerClient(string projectRoot)
    {
        _store = new CodexProjectStore(projectRoot);
        Settings = _store.LoadSettings();
        _session = _store.LoadSession();
    }

    public CodexClientSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new CodexClientSnapshot(
                _connectionStatus,
                _statusText,
                _session.ThreadId,
                _activeTurnId,
                !string.IsNullOrWhiteSpace(_activeTurnId),
                _authMode,
                _planType,
                _loginUrl,
                _session.Transcript.Select(entry => entry.Copy()).ToArray(),
                _approvals.ToArray(),
                _revision);
        }
    }

    public Task StartAsync()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _startTask ??= StartCoreAsync();
        }
    }

    public async Task SendTurnAsync(string prompt, IEnumerable<string>? contextPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        await StartAsync().ConfigureAwait(false);
        EnsureReady();

        string threadId;
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(_activeTurnId))
            {
                throw new InvalidOperationException("A Codex turn is already running.");
            }
            threadId = _session.ThreadId;
        }
        if (string.IsNullOrWhiteSpace(threadId)) threadId = await StartThreadAsync().ConfigureAwait(false);

        var text = BuildPrompt(prompt.Trim(), contextPaths);
        AddOrUpdateTranscript(new CodexTranscriptEntry
        {
            Kind = CodexTranscriptKind.User,
            Text = prompt.Trim(),
            Status = "completed"
        });
        SaveSession();

        var parameters = new Dictionary<string, object?>
        {
            ["threadId"] = threadId,
            ["input"] = new[] { new Dictionary<string, object?> { ["type"] = "text", ["text"] = text } },
            ["cwd"] = ProjectRoot,
            ["approvalPolicy"] = Settings.ApprovalPolicy,
            ["sandboxPolicy"] = new Dictionary<string, object?>
            {
                ["type"] = "workspaceWrite",
                ["writableRoots"] = new[] { ProjectRoot },
                ["networkAccess"] = Settings.NetworkAccess
            }
        };
        if (!string.IsNullOrWhiteSpace(Settings.Model)) parameters["model"] = Settings.Model.Trim();
        if (!string.IsNullOrWhiteSpace(Settings.Effort)) parameters["effort"] = Settings.Effort.Trim();

        try
        {
            SetStatus("Codex is working");
            var result = await SendRequestAsync("turn/start", parameters).ConfigureAwait(false);
            var turnId = result.TryGetProperty("turn", out var turn)
                ? ReadString(turn, "id")
                : string.Empty;
            lock (_sync)
            {
                _activeTurnId = turnId;
                _revision++;
            }
            RaiseChanged();
        }
        catch (Exception exception)
        {
            lock (_sync) _activeTurnId = string.Empty;
            AddError(exception.Message);
            throw;
        }
    }

    public async Task NewConversationAsync()
    {
        await StartAsync().ConfigureAwait(false);
        EnsureReady();
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(_activeTurnId))
            {
                throw new InvalidOperationException("Stop the active turn before starting a new conversation.");
            }
            _session.ThreadId = string.Empty;
            _session.Transcript.Clear();
            _approvals.Clear();
            _revision++;
        }
        SaveSession();
        await StartThreadAsync().ConfigureAwait(false);
    }

    public async Task InterruptAsync()
    {
        string threadId;
        string turnId;
        lock (_sync)
        {
            threadId = _session.ThreadId;
            turnId = _activeTurnId;
        }
        if (string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(turnId)) return;
        await SendRequestAsync("turn/interrupt", new { threadId, turnId }).ConfigureAwait(false);
    }

    public async Task<string> BeginChatGptLoginAsync()
    {
        await StartAsync().ConfigureAwait(false);
        EnsureReady();
        var result = await SendRequestAsync("account/login/start", new
        {
            type = "chatgpt",
            useHostedLoginSuccessPage = true,
            appBrand = "codex"
        }).ConfigureAwait(false);
        var url = ReadString(result, "authUrl");
        lock (_sync)
        {
            _loginUrl = url;
            _statusText = string.IsNullOrWhiteSpace(url) ? "Waiting for sign in" : "Complete sign in in your browser";
            _revision++;
        }
        RaiseChanged();
        return url;
    }

    public async Task RespondToApprovalAsync(CodexApprovalRequest request, string decision)
    {
        ArgumentNullException.ThrowIfNull(request);
        var response = CodexProtocol.BuildApprovalResponse(request.RequestIdJson, decision);
        await SendRawAsync(response).ConfigureAwait(false);
        lock (_sync)
        {
            _approvals.RemoveAll(item => item.RequestIdJson == request.RequestIdJson);
            _revision++;
        }
        RaiseChanged();
    }

    public void SaveSettings() => _store.SaveSettings(Settings);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }

        SaveSession();
        _lifetime.Cancel();
        try { _writer?.Close(); } catch (IOException) { }
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        _process?.Dispose();
        _writeLock.Dispose();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task StartCoreAsync()
    {
        SetConnection(CodexConnectionStatus.Starting, "Starting Codex app server");
        try
        {
            var executable = ResolveExecutable();
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = ProjectRoot,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("app-server");
            try
            {
                _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Codex did not start.");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new InvalidOperationException(
                    "The Codex CLI could not be launched. Install it or set its full path in Codex Settings.");
            }
            _writer = _process.StandardInput;
            _writer.AutoFlush = true;
            _ = ReadOutputAsync(_process.StandardOutput, _lifetime.Token);
            _ = ReadErrorsAsync(_process.StandardError, _lifetime.Token);

            await SendRequestAsync("initialize", new
            {
                clientInfo = new
                {
                    name = "bengine_editor",
                    title = "BEngine Codex Editor",
                    version = "0.1.0"
                }
            }).ConfigureAwait(false);
            await SendNotificationAsync("initialized", new { }).ConfigureAwait(false);
            SetConnection(CodexConnectionStatus.Ready, "Connected");
            await RefreshAccountAsync().ConfigureAwait(false);
            await TryResumeThreadAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            SetConnection(CodexConnectionStatus.Faulted, exception.Message);
            AddError($"Could not start Codex: {exception.Message}");
        }
    }

    private async Task RefreshAccountAsync()
    {
        var result = await SendRequestAsync("account/read", new { refreshToken = false }).ConfigureAwait(false);
        var mode = string.Empty;
        var plan = string.Empty;
        if (result.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object)
        {
            mode = ReadString(account, "type");
            plan = ReadString(account, "planType");
        }
        var requiresAuth = result.TryGetProperty("requiresOpenaiAuth", out var requires) &&
                           requires.ValueKind == JsonValueKind.True;
        lock (_sync)
        {
            _authMode = mode;
            _planType = plan;
            _statusText = requiresAuth && string.IsNullOrWhiteSpace(mode) ? "Sign in required" : "Ready";
            _revision++;
        }
        RaiseChanged();
    }

    private async Task TryResumeThreadAsync()
    {
        string threadId;
        lock (_sync) threadId = _session.ThreadId;
        if (string.IsNullOrWhiteSpace(threadId)) return;
        try
        {
            await SendRequestAsync("thread/resume", new { threadId }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                _session.ThreadId = string.Empty;
                _revision++;
            }
            AddOrUpdateTranscript(new CodexTranscriptEntry
            {
                Kind = CodexTranscriptKind.Activity,
                Text = $"Previous conversation could not be resumed: {exception.Message}",
                Status = "failed"
            });
            SaveSession();
        }
    }

    private async Task<string> StartThreadAsync()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["cwd"] = ProjectRoot,
            ["approvalPolicy"] = Settings.ApprovalPolicy,
            ["sandbox"] = "workspaceWrite",
            ["serviceName"] = "bengine_editor"
        };
        if (!string.IsNullOrWhiteSpace(Settings.Model)) parameters["model"] = Settings.Model.Trim();
        var result = await SendRequestAsync("thread/start", parameters).ConfigureAwait(false);
        var threadId = result.TryGetProperty("thread", out var thread)
            ? ReadString(thread, "id")
            : string.Empty;
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidDataException("Codex returned an empty thread id.");
        }
        lock (_sync)
        {
            _session.ThreadId = threadId;
            _revision++;
        }
        SaveSession();
        RaiseChanged();
        return threadId;
    }

    private async Task<JsonElement> SendRequestAsync(string method, object parameters)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRequests.TryAdd(id, completion)) throw new InvalidOperationException("Duplicate Codex request id.");
        try
        {
            await SendRawAsync(JsonSerializer.Serialize(new { method, id, @params = parameters }, JsonOptions))
                .ConfigureAwait(false);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(30), _lifetime.Token).ConfigureAwait(false);
        }
        finally
        {
            _pendingRequests.TryRemove(id, out _);
        }
    }

    private Task SendNotificationAsync(string method, object parameters) =>
        SendRawAsync(JsonSerializer.Serialize(new { method, @params = parameters }, JsonOptions));

    private async Task SendRawAsync(string json)
    {
        var writer = _writer ?? throw new InvalidOperationException("Codex app server is not connected.");
        await _writeLock.WaitAsync(_lifetime.Token).ConfigureAwait(false);
        try
        {
            await writer.WriteLineAsync(json.AsMemory(), _lifetime.Token).ConfigureAwait(false);
            await writer.FlushAsync(_lifetime.Token).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;
                HandleMessage(line);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!cancellationToken.IsCancellationRequested) AddError($"Codex connection failed: {exception.Message}");
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                SetConnection(CodexConnectionStatus.Faulted, "Codex app server stopped");
            }
        }
    }

    private async Task ReadErrorsAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;
                if (line.Contains("error", StringComparison.OrdinalIgnoreCase))
                {
                    AddError(line.Trim());
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void HandleMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("method", out _))
            {
                HandleServerEvent(root, json);
                return;
            }
            if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id) ||
                !_pendingRequests.TryRemove(id, out var completion)) return;
            if (root.TryGetProperty("error", out var error))
            {
                completion.TrySetException(new InvalidOperationException(ReadString(error, "message", error.ToString())));
            }
            else if (root.TryGetProperty("result", out var result)) completion.TrySetResult(result.Clone());
            else completion.TrySetResult(default);
        }
        catch (Exception exception)
        {
            AddError($"Invalid Codex protocol message: {exception.Message}");
        }
    }

    private void HandleServerEvent(JsonElement root, string json)
    {
        var method = ReadString(root, "method");
        if (root.TryGetProperty("id", out _))
        {
            if (method is "item/commandExecution/requestApproval" or "item/fileChange/requestApproval")
            {
                ApplyProtocolEvent(CodexProtocol.ParseEvent(json));
            }
            else
            {
                _ = RespondUnsupportedRequestAsync(root, method);
            }
            return;
        }
        ApplyProtocolEvent(CodexProtocol.ParseEvent(json));
    }

    private async Task RespondUnsupportedRequestAsync(JsonElement root, string method)
    {
        var id = root.GetProperty("id").GetRawText();
        string resultJson;
        if (method == "item/permissions/requestApproval") resultJson = "{\"permissions\":{}}";
        else if (method == "tool/requestUserInput") resultJson = "{\"answers\":{}}";
        else
        {
            using var idDocument = JsonDocument.Parse(id);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("id");
                idDocument.RootElement.WriteTo(writer);
                writer.WritePropertyName("error");
                writer.WriteStartObject();
                writer.WriteNumber("code", -32601);
                writer.WriteString("message", $"BEngine does not support server request '{method}'.");
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            await SendRawAsync(System.Text.Encoding.UTF8.GetString(stream.ToArray())).ConfigureAwait(false);
            return;
        }
        await SendRawAsync($"{{\"id\":{id},\"result\":{resultJson}}}").ConfigureAwait(false);
    }

    private void ApplyProtocolEvent(CodexProtocolEvent protocolEvent)
    {
        switch (protocolEvent.Kind)
        {
            case CodexProtocolEventKind.AgentDelta:
                AppendAgentDelta(protocolEvent.ItemId, protocolEvent.Text);
                break;
            case CodexProtocolEventKind.AgentCompleted:
                AddOrUpdateTranscript(new CodexTranscriptEntry
                {
                    Id = protocolEvent.ItemId,
                    Kind = CodexTranscriptKind.Assistant,
                    Text = protocolEvent.Text,
                    Status = protocolEvent.Status
                });
                break;
            case CodexProtocolEventKind.ActivityStarted:
            case CodexProtocolEventKind.ActivityCompleted:
                AddOrUpdateTranscript(new CodexTranscriptEntry
                {
                    Id = protocolEvent.ItemId,
                    Kind = CodexTranscriptKind.Activity,
                    Text = protocolEvent.Text,
                    Status = protocolEvent.Status
                });
                break;
            case CodexProtocolEventKind.PlanUpdated:
                AddOrUpdateTranscript(new CodexTranscriptEntry
                {
                    Id = $"plan:{protocolEvent.TurnId}",
                    Kind = CodexTranscriptKind.Plan,
                    Text = protocolEvent.Text,
                    Status = "inProgress"
                });
                break;
            case CodexProtocolEventKind.TurnStarted:
                lock (_sync)
                {
                    _activeTurnId = protocolEvent.TurnId;
                    _statusText = "Codex is working";
                    _revision++;
                }
                RaiseChanged();
                break;
            case CodexProtocolEventKind.TurnCompleted:
                lock (_sync)
                {
                    _activeTurnId = string.Empty;
                    _statusText = protocolEvent.Status == "failed" ? "Turn failed" : "Ready";
                    _approvals.Clear();
                    _revision++;
                }
                if (!string.IsNullOrWhiteSpace(protocolEvent.Text)) AddError(protocolEvent.Text);
                SaveSession();
                RaiseChanged();
                break;
            case CodexProtocolEventKind.Error:
                AddError(protocolEvent.Text);
                break;
            case CodexProtocolEventKind.Warning:
                AddOrUpdateTranscript(new CodexTranscriptEntry
                {
                    Kind = CodexTranscriptKind.Activity,
                    Text = protocolEvent.Text,
                    Status = "warning"
                });
                break;
            case CodexProtocolEventKind.AccountUpdated:
                lock (_sync)
                {
                    _authMode = protocolEvent.AuthMode;
                    _planType = protocolEvent.PlanType;
                    _loginUrl = string.Empty;
                    _statusText = string.IsNullOrWhiteSpace(_authMode) ? "Sign in required" : "Ready";
                    _revision++;
                }
                RaiseChanged();
                break;
            case CodexProtocolEventKind.LoginCompleted:
                SetStatus(protocolEvent.Status == "completed" ? "Signed in" : protocolEvent.Text);
                break;
            case CodexProtocolEventKind.ApprovalRequested when protocolEvent.Approval is { } approval:
                lock (_sync)
                {
                    _approvals.RemoveAll(item => item.RequestIdJson == approval.RequestIdJson);
                    _approvals.Add(approval);
                    _statusText = "Approval required";
                    _revision++;
                }
                RaiseChanged();
                break;
        }
    }

    private void AppendAgentDelta(string itemId, string delta)
    {
        lock (_sync)
        {
            var entry = _session.Transcript.FirstOrDefault(item => item.Id == itemId);
            if (entry is null)
            {
                entry = new CodexTranscriptEntry
                {
                    Id = string.IsNullOrWhiteSpace(itemId) ? Guid.NewGuid().ToString("N") : itemId,
                    Kind = CodexTranscriptKind.Assistant,
                    Status = "inProgress"
                };
                _session.Transcript.Add(entry);
            }
            entry.Text += delta;
            _revision++;
        }
        RaiseChanged();
    }

    private void AddOrUpdateTranscript(CodexTranscriptEntry entry)
    {
        lock (_sync)
        {
            var existing = _session.Transcript.FirstOrDefault(item => item.Id == entry.Id);
            if (existing is null) _session.Transcript.Add(entry);
            else
            {
                existing.Kind = entry.Kind;
                existing.Text = entry.Text;
                existing.Status = entry.Status;
            }
            _revision++;
        }
        RaiseChanged();
    }

    private string BuildPrompt(string prompt, IEnumerable<string>? contextPaths)
    {
        var contexts = (contextPaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(_store.NormalizeContextPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (contexts.Length == 0) return prompt;
        return $"{prompt}{Environment.NewLine}{Environment.NewLine}Project context:{Environment.NewLine}" +
               string.Join(Environment.NewLine, contexts.Select(path => $"- {path}"));
    }

    private string ResolveExecutable()
    {
        var configured = Settings.ExecutablePath.Trim();
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable("BENGINE_CODEX_PATH")?.Trim() ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(configured)) return "codex";
        var fullPath = Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(ProjectRoot, configured));
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Configured Codex executable was not found.", fullPath);
        return fullPath;
    }

    private void EnsureReady()
    {
        lock (_sync)
        {
            if (_connectionStatus != CodexConnectionStatus.Ready)
            {
                throw new InvalidOperationException(_statusText);
            }
        }
    }

    private void AddError(string text) => AddOrUpdateTranscript(new CodexTranscriptEntry
    {
        Kind = CodexTranscriptKind.Error,
        Text = text,
        Status = "failed"
    });

    private void SetConnection(CodexConnectionStatus status, string text)
    {
        lock (_sync)
        {
            _connectionStatus = status;
            _statusText = text;
            if (status == CodexConnectionStatus.Faulted) _activeTurnId = string.Empty;
            _revision++;
        }
        RaiseChanged();
    }

    private void SetStatus(string text)
    {
        lock (_sync)
        {
            _statusText = text;
            _revision++;
        }
        RaiseChanged();
    }

    private void SaveSession()
    {
        CodexSessionDocument copy;
        lock (_sync)
        {
            copy = new CodexSessionDocument
            {
                ThreadId = _session.ThreadId,
                Transcript = _session.Transcript.Select(entry => entry.Copy()).ToList()
            };
        }
        _store.SaveSession(copy);
    }

    private void RaiseChanged() => changed?.Invoke();

    private static string ReadString(JsonElement element, string property, string fallback = "") =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value.ToString()
            : fallback;
}
