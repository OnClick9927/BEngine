using System.Text.Json;

namespace BEngine.Editor.Codex;

public sealed partial class CodexAppServerClient
{
    private readonly List<CodexThreadSummary> _threads = [];
    private readonly List<CodexModelOption> _models = [];
    private readonly List<CodexUserInputRequest> _questions = [];
    private readonly Queue<string> _resumeEvents = new();
    private CodexClientSnapshot? _cachedSnapshot;
    private string _nextThreadCursor = string.Empty;
    private string _threadTitle = string.Empty;
    private string _modelCatalogError = string.Empty;
    private string _historyCatalogError = string.Empty;
    private string _loadingThreadId = string.Empty;
    private string _lastCompletedTurnId = string.Empty;
    private bool _showingArchived;
    private bool _turnPending;

    public async Task RefreshThreadsAsync(bool archived = false, bool loadMore = false)
    {
        await StartAsync().ConfigureAwait(false);
        EnsureReady();
        await RefreshThreadsCoreAsync(archived, loadMore).ConfigureAwait(false);
    }

    public async Task ResumeThreadAsync(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        await StartAsync().ConfigureAwait(false);
        if (GetSnapshot().ThreadId == threadId) return;
        await StopForConversationSwitchAsync().ConfigureAwait(false);
        EnsureIdle();
        SaveSession();
        await ResumeThreadCoreAsync(threadId).ConfigureAwait(false);
    }

    public async Task RenameThreadAsync(string threadId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await StartAsync().ConfigureAwait(false);
        EnsureReady();
        await SendRequestAsync("thread/name/set", new { threadId, name = name.Trim() }).ConfigureAwait(false);
        lock (_sync)
        {
            if (_session.ThreadId == threadId) _threadTitle = name.Trim();
            var index = _threads.FindIndex(item => item.Id == threadId);
            if (index >= 0) _threads[index] = _threads[index] with { Title = name.Trim() };
            _revision++;
        }
        RaiseChanged();
    }

    public async Task ArchiveThreadAsync(string threadId, bool archive)
    {
        await StartAsync().ConfigureAwait(false);
        EnsureIdle();
        await SendRequestAsync(archive ? "thread/archive" : "thread/unarchive", new { threadId }).ConfigureAwait(false);
        lock (_sync)
        {
            if (archive && _session.ThreadId == threadId)
            {
                _session.ThreadId = string.Empty; _session.Transcript.Clear(); _threadTitle = string.Empty;
            }
            _revision++;
        }
        SaveSession();
        await RefreshThreadsCoreAsync(_showingArchived, false).ConfigureAwait(false);
    }

    public async Task SteerTurnAsync(string prompt, IEnumerable<string>? contextPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        await StartAsync().ConfigureAwait(false);
        EnsureReady();
        string threadId, expectedTurnId;
        lock (_sync) { threadId = _session.ThreadId; expectedTurnId = _activeTurnId; }
        if (string.IsNullOrWhiteSpace(expectedTurnId)) throw new InvalidOperationException("There is no active turn to follow up.");
        await SendRequestAsync("turn/steer", new
        {
            threadId, expectedTurnId,
            input = BuildInput(prompt.Trim(), contextPaths)
        }).ConfigureAwait(false);
        AddOrUpdateTranscript(new CodexTranscriptEntry { Kind = CodexTranscriptKind.User, Text = prompt.Trim(), Status = "completed" });
        SaveSession();
    }

    public async Task AnswerQuestionsAsync(CodexUserInputRequest request, IReadOnlyDictionary<string, string> answers)
    {
        var payload = request.Questions.ToDictionary(question => question.Id, question => new
        {
            answers = answers.TryGetValue(question.Id, out var value) && !string.IsNullOrWhiteSpace(value)
                ? new[] { value.Trim() } : Array.Empty<string>()
        });
        await SendServerResultAsync(request.RequestIdJson, new { answers = payload }).ConfigureAwait(false);
        lock (_sync)
        {
            _questions.RemoveAll(item => item.RequestIdJson == request.RequestIdJson);
            UpdateRequestStatus();
            _revision++;
        }
        RaiseChanged();
    }

    private Task SendServerResultAsync(string idJson, object result)
    {
        using var id = JsonDocument.Parse(idJson);
        return SendRawAsync(JsonSerializer.Serialize(new { id = id.RootElement, result }, JsonOptions));
    }

    private void EnsureIdle()
    {
        EnsureReady();
        lock (_sync)
            if (_turnPending || !string.IsNullOrWhiteSpace(_activeTurnId))
                throw new InvalidOperationException("Stop the active turn before switching conversations.");
    }

    private async Task RefreshCatalogsCoreAsync()
    {
        try
        {
            var models = new List<CodexModelOption>();
            string? cursor = null;
            do
            {
                var result = await SendRequestAsync("model/list", new { limit = 100, includeHidden = false, cursor }).ConfigureAwait(false);
                if (result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    foreach (var model in data.EnumerateArray())
                    {
                        var efforts = model.TryGetProperty("supportedReasoningEfforts", out var supported) && supported.ValueKind == JsonValueKind.Array
                            ? supported.EnumerateArray().Select(item => ReadString(item, "reasoningEffort")).ToArray() : [];
                        var id = ReadString(model, "model", ReadString(model, "id"));
                        models.Add(new CodexModelOption(id, ReadString(model, "displayName", id),
                            ReadString(model, "defaultReasoningEffort"), efforts,
                            model.TryGetProperty("isDefault", out var isDefault) && isDefault.ValueKind == JsonValueKind.True));
                    }
                var next = ReadString(result, "nextCursor");
                if (string.IsNullOrEmpty(next) || next == cursor) break;
                cursor = next;
            } while (true);
            lock (_sync) { _models.Clear(); _models.AddRange(models); _modelCatalogError = string.Empty; _revision++; }
        }
        catch (Exception exception) { SetCatalogError("Models: " + exception.Message, models: true); }
        await RefreshThreadsCoreAsync(false, false).ConfigureAwait(false);
    }

    private async Task RefreshThreadsCoreAsync(bool archived, bool loadMore)
    {
        try
        {
            string? cursor;
            lock (_sync) cursor = loadMore && archived == _showingArchived ? _nextThreadCursor : null;
            var result = await SendRequestAsync("thread/list", new
            {
                cwd = ProjectRoot, archived, cursor, limit = 50, sortKey = "updated_at",
                sourceKinds = new[] { "appServer", "cli", "vscode" }
            }).ConfigureAwait(false);
            lock (_sync)
            {
                if (!loadMore || archived != _showingArchived) _threads.Clear();
                _showingArchived = archived;
                _nextThreadCursor = ReadString(result, "nextCursor");
                if (result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    foreach (var thread in data.EnumerateArray())
                    {
                        var summary = ReadThreadSummary(thread);
                        _threads.RemoveAll(item => item.Id == summary.Id);
                        _threads.Add(summary);
                    }
                _historyCatalogError = string.Empty;
                _revision++;
            }
            RaiseChanged();
        }
        catch (Exception exception) { SetCatalogError("Conversations: " + exception.Message); }
    }

    private async Task ResumeThreadCoreAsync(string threadId)
    {
        lock (_sync) { _loadingThreadId = threadId; _resumeEvents.Clear(); }
        try
        {
            var result = await SendRequestAsync("thread/resume", new
            {
                threadId, cwd = ProjectRoot, developerInstructions = BuildProjectInstructions()
            }).ConfigureAwait(false);
            if (!result.TryGetProperty("thread", out var thread))
                throw new InvalidDataException("Codex did not return the conversation.");
            var transcript = ReadTranscript(thread);
            lock (_sync)
            {
                _session.ThreadId = threadId;
                _session.Transcript = transcript;
                _threadTitle = ReadThreadSummary(thread).Title;
                _activeTurnId = string.Empty;
                _lastCompletedTurnId = string.Empty;
                _approvals.Clear(); _questions.Clear();
                if (thread.TryGetProperty("turns", out var turns) && turns.ValueKind == JsonValueKind.Array)
                    foreach (var turn in turns.EnumerateArray())
                        if (ReadString(turn, "status") == "inProgress") _activeTurnId = ReadString(turn, "id");
                _loadingThreadId = string.Empty;
                _statusText = _activeTurnId.Length == 0 ? "Ready" : "Codex is working";
                _revision++;
                while (_resumeEvents.TryDequeue(out var message)) HandleMessage(message);
            }
            SaveSession(); RaiseChanged();
            await AssignThreadProjectAsync(threadId).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync) { _loadingThreadId = string.Empty; _resumeEvents.Clear(); }
        }
    }

    internal static List<CodexTranscriptEntry> ReadTranscript(JsonElement thread)
    {
        var result = new List<CodexTranscriptEntry>();
        if (!thread.TryGetProperty("turns", out var turns) || turns.ValueKind != JsonValueKind.Array) return result;
        foreach (var turn in turns.EnumerateArray())
        {
            if (!turn.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in items.EnumerateArray())
            {
                if (ReadString(item, "type") == "userMessage")
                {
                    var text = item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
                        ? string.Join("\n", content.EnumerateArray().Select((part, index) => (Text: ReadString(part, "text"), Index: index))
                            .Where(part => part.Index == 0 || !part.Text.StartsWith(ContextPrefix, StringComparison.Ordinal))
                            .Select(part => part.Text)) : string.Empty;
                    result.Add(new CodexTranscriptEntry { Id = ReadString(item, "id"), Kind = CodexTranscriptKind.User, Text = text, Status = "completed" });
                    continue;
                }
                var message = JsonSerializer.Serialize(new { method = "item/completed", @params = new { item } }, JsonOptions);
                var parsed = CodexProtocol.ParseEvent(message);
                if (parsed.Kind is not (CodexProtocolEventKind.AgentCompleted or CodexProtocolEventKind.ActivityCompleted)) continue;
                result.Add(new CodexTranscriptEntry { Id = parsed.ItemId, Text = parsed.Text, Status = parsed.Status,
                    Kind = parsed.Kind == CodexProtocolEventKind.AgentCompleted ? CodexTranscriptKind.Assistant : CodexTranscriptKind.Activity });
            }
        }
        return result;
    }

    private static CodexThreadSummary ReadThreadSummary(JsonElement thread)
    {
        var title = ReadString(thread, "name");
        if (string.IsNullOrWhiteSpace(title)) title = ReadString(thread, "preview", "New conversation");
        var contextIndex = title.IndexOf("[BEngine project context]", StringComparison.Ordinal);
        if (contextIndex >= 0) title = title[..contextIndex];
        title = title.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return new CodexThreadSummary(ReadString(thread, "id"), string.IsNullOrEmpty(title) ? "New conversation" : title,
            thread.TryGetProperty("updatedAt", out var updated) && updated.TryGetInt64(out var timestamp) ? timestamp : 0);
    }

    private void UpdateCurrentThreadTitle()
    {
        if (string.IsNullOrWhiteSpace(_threadTitle))
            _threadTitle = _session.Transcript.FirstOrDefault(item => item.Kind == CodexTranscriptKind.User)?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_session.ThreadId) || _showingArchived) return;
        _threads.RemoveAll(item => item.Id == _session.ThreadId);
        _threads.Insert(0, new CodexThreadSummary(_session.ThreadId, _threadTitle,
            EditorHostServices.Platform.UtcNow.ToUnixTimeSeconds()));
    }

    private void SetCatalogError(string message, bool models = false)
    {
        lock (_sync)
        {
            if (models) _modelCatalogError = message; else _historyCatalogError = message;
            _revision++;
        }
        RaiseChanged();
    }

    private bool HandleConversationEvent(JsonElement root, string json, string method)
    {
        if (!root.TryGetProperty("params", out var parameters)) return false;
        var threadId = ReadString(parameters, "threadId");
        lock (_sync)
        {
            if (_loadingThreadId.Length > 0 && threadId == _loadingThreadId)
            {
                _resumeEvents.Enqueue(json); return true;
            }
            if (threadId.Length > 0 && threadId != _session.ThreadId) return true;
        }
        if (method is "item/tool/requestUserInput" or "tool/requestUserInput")
        {
            var questions = new List<CodexQuestion>();
            if (parameters.TryGetProperty("questions", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var question in list.EnumerateArray())
                {
                    var options = question.TryGetProperty("options", out var choices) && choices.ValueKind == JsonValueKind.Array
                        ? choices.EnumerateArray().Select(item => new CodexQuestionOption(ReadString(item, "label"), ReadString(item, "description"))).ToArray() : [];
                    questions.Add(new CodexQuestion(ReadString(question, "id"), ReadString(question, "header"), ReadString(question, "question"),
                        question.TryGetProperty("isSecret", out var secret) && secret.ValueKind == JsonValueKind.True, options));
                }
            lock (_sync)
            {
                _questions.Add(new CodexUserInputRequest(root.GetProperty("id").GetRawText(), questions));
                _statusText = "Waiting for your response"; _revision++;
            }
            RaiseChanged(); return true;
        }
        if (method == "serverRequest/resolved")
        {
            if (parameters.TryGetProperty("requestId", out var id))
            {
                lock (_sync)
                {
                    _questions.RemoveAll(item => item.RequestIdJson == id.GetRawText());
                    _approvals.RemoveAll(item => item.RequestIdJson == id.GetRawText());
                    UpdateRequestStatus();
                    _revision++;
                }
                RaiseChanged();
            }
            return true;
        }
        if (method == "thread/name/updated")
        {
            lock (_sync) { _threadTitle = ReadString(parameters, "threadName", ReadString(parameters, "name")); UpdateCurrentThreadTitle(); _revision++; }
            RaiseChanged(); return true;
        }
        if (method == "turn/diff/updated")
        {
            AddOrUpdateTranscript(new CodexTranscriptEntry { Id = "diff:" + ReadString(parameters, "turnId"),
                Kind = CodexTranscriptKind.Diff, Text = ReadString(parameters, "diff"), Status = "completed" });
            return true;
        }
        if (method == "item/commandExecution/outputDelta")
        {
            lock (_sync)
            {
                var item = _session.Transcript.FirstOrDefault(entry => entry.Id == ReadString(parameters, "itemId"));
                if (item is not null)
                {
                    if (!item.Text.Contains('\n')) item.Text += "\n";
                    item.Text += ReadString(parameters, "delta");
                    if (item.Text.Length > 65536) item.Text = item.Text[..4096] + "\n[output truncated]\n" + item.Text[^57344..];
                    _revision++;
                }
            }
            RaiseChanged(); return true;
        }
        return false;
    }

    private void UpdateRequestStatus()
    {
        _statusText = _approvals.Count > 0 ? "Approval required" :
            _questions.Count > 0 ? "Waiting for your response" :
            _activeTurnId.Length > 0 ? "Codex is working" : "Ready";
    }
}
