using System.Net;
using System.Net.Sockets;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using BEngine.HotUpdate;
using BEngine.Rendering.Rhi;

namespace BEngine.Diagnostics;

public sealed class RemoteDiagnosticsServer : IDisposable
{
    public const int ProtocolVersion = 1;
    public const int MaximumMessageBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TcpListener _listener;
    private readonly string _token;
    private readonly CancellationTokenSource _stop = new();
    private readonly BTask _acceptLoop;
    private readonly HashSet<TcpClient> _clients = [];
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly IDisposable _gpuTimingLease;
    private int _disposed;

    private RemoteDiagnosticsServer(TcpListener listener, string token)
    {
        _listener = listener;
        _token = token;
        _ = RuntimeDiagnosticsTelemetry.Capture();
        _gpuTimingLease = GraphicsFrameTimingService.Acquire();
        _acceptLoop = AcceptLoopAsync();
    }

    public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;

    public static RemoteDiagnosticsServer Start(
        IPAddress address,
        int port,
        string token)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        token = token?.Trim() ?? string.Empty;
        if (!IPAddress.IsLoopback(address) && token.Length < 16)
            throw new ArgumentException(
                "A token of at least 16 characters is required for non-loopback diagnostics.",
                nameof(token));
        // Browsers cannot open TCP listeners. Check the platform before allocating
        // the server so Web builds can also discard its unreachable TCP protocol.
        if (OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("TCP diagnostics is not supported in the browser.");
        var listener = new TcpListener(address, port);
        listener.Start(16);
        return new RemoteDiagnosticsServer(listener, token);
    }

    private async BTask AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            await BTask.Yield();
            if (_stop.IsCancellationRequested) break;
            if (_clients.Count >= 16) continue;
            TcpClient client;
            try { client = await OnFrame(_listener.AcceptTcpClientAsync(_stop.Token).AsTask()); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            _clients.Add(client);
            _ = HandleClientAsync(client);
        }
    }

    private async BTask HandleClientAsync(TcpClient client)
    {
        try
        {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true)
               { AutoFlush = true })
        {
            while (!_stop.IsCancellationRequested)
            {
                await BTask.Yield();
                if (_stop.IsCancellationRequested) break;
                string? line;
                try { line = await OnFrame(reader.ReadLineAsync(_stop.Token).AsTask()); }
                catch (OperationCanceledException) { break; }
                catch (IOException) { break; }
                if (line is null) break;
                if (Encoding.UTF8.GetByteCount(line) > MaximumMessageBytes)
                {
                    await WriteAsync(writer, RemoteResponse.Failure("Message exceeds the protocol limit."));
                    break;
                }
                RemoteRequest? request;
                try { request = JsonSerializer.Deserialize<RemoteRequest>(line, JsonOptions); }
                catch (JsonException exception)
                {
                    await WriteAsync(writer, RemoteResponse.Failure(exception.Message));
                    continue;
                }
                if (request is null || request.Protocol != ProtocolVersion)
                {
                    await WriteAsync(writer, RemoteResponse.Failure(
                        $"Diagnostics protocol {ProtocolVersion} is required."));
                    continue;
                }
                if (!FixedTimeEquals(request.Token ?? string.Empty, _token))
                {
                    await WriteAsync(writer, RemoteResponse.Failure("Authentication failed."));
                    break;
                }
                if (Environment.CurrentManagedThreadId != _ownerThread)
                    throw new InvalidOperationException("Diagnostics capture must run on its owning engine thread.");
                var response = request.Operation switch
                {
                    "hello" => RemoteResponse.Success(new RemoteHello(
                        ProtocolVersion, Environment.ProcessId, Environment.MachineName)),
                    "sample" => RemoteResponse.Success(RuntimeDiagnosticsSample.Capture()),
                    "heap" => RemoteResponse.Success(EngineObjectHeapSnapshot.Capture(
                        request.ForceGarbageCollection)),
                    _ => RemoteResponse.Failure($"Unknown operation '{request.Operation}'.")
                };
                await WriteAsync(writer, response);
            }
        }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally { _clients.Remove(client); }
    }

    private static async BTask WriteAsync(StreamWriter writer, RemoteResponse response)
    {
        var pending = writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
        while (!pending.IsCompleted) await BTask.Yield();
        pending.GetAwaiter().GetResult();
    }

    private static async BTask<T> OnFrame<T>(Task<T> pending)
    {
        while (!pending.IsCompleted) await BTask.Yield();
        return pending.GetAwaiter().GetResult();
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            leftBytes, rightBytes);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _listener.Stop();
        foreach (var client in _clients.ToArray()) client.Dispose();
        _clients.Clear();
        _gpuTimingLease.Dispose();
        _stop.Dispose();
    }

    private sealed record RemoteRequest(
        int Protocol,
        string Operation,
        string Token,
        bool ForceGarbageCollection = false);

    private sealed record RemoteResponse(bool Ok, string Error, JsonElement Data)
    {
        internal static RemoteResponse Success<T>(T value) =>
            new(true, string.Empty, JsonSerializer.SerializeToElement(value, JsonOptions));
        internal static RemoteResponse Failure(string error) =>
            new(false, error, JsonSerializer.SerializeToElement<object?>(null, JsonOptions));
    }
}

public sealed class RemoteDiagnosticsClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TcpClient _client;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly string _token;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private int _disposed;

    private RemoteDiagnosticsClient(TcpClient client, string token)
    {
        _client = client;
        _token = token;
        var stream = client.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
        _writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true)
            { AutoFlush = true };
    }

    public RemoteHello Hello { get; private set; } = new(0, 0, string.Empty);

    public static async Task<RemoteDiagnosticsClient> ConnectAsync(
        string host,
        int port,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(host.Trim(), port, cancellationToken).ConfigureAwait(false);
            var result = new RemoteDiagnosticsClient(client, token?.Trim() ?? string.Empty);
            result.Hello = await result.RequestAsync<RemoteHello>(
                "hello", false, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public Task<RuntimeDiagnosticsSample> CaptureSampleAsync(
        CancellationToken cancellationToken = default) =>
        RequestAsync<RuntimeDiagnosticsSample>("sample", false, cancellationToken);

    public Task<EngineObjectHeapSnapshot> CaptureHeapAsync(
        bool forceGarbageCollection = false,
        CancellationToken cancellationToken = default) =>
        RequestAsync<EngineObjectHeapSnapshot>("heap", forceGarbageCollection, cancellationToken);

    private async Task<T> RequestAsync<T>(
        string operation,
        bool forceGarbageCollection,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _requests.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var request = new
            {
                protocol = RemoteDiagnosticsServer.ProtocolVersion,
                operation,
                token = _token,
                forceGarbageCollection
            };
            await _writer.WriteLineAsync(
                JsonSerializer.Serialize(request, JsonOptions).AsMemory(),
                cancellationToken).ConfigureAwait(false);
            var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) ??
                       throw new EndOfStreamException("Remote diagnostics connection closed.");
            if (Encoding.UTF8.GetByteCount(line) > RemoteDiagnosticsServer.MaximumMessageBytes)
                throw new InvalidDataException("Remote diagnostics response exceeds the protocol limit.");
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.GetProperty("ok").GetBoolean())
                throw new InvalidOperationException(root.GetProperty("error").GetString() ??
                                                    "Remote diagnostics request failed.");
            return root.GetProperty("data").Deserialize<T>(JsonOptions) ??
                   throw new InvalidDataException("Remote diagnostics response has no data.");
        }
        finally { _requests.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _writer.Dispose();
            _reader.Dispose();
            _client.Dispose();
            _requests.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}

public sealed record RemoteHello(int Protocol, int ProcessId, string MachineName);

public static class RemoteDiagnosticsHost
{
    public static void StartFromEnvironment() => RemoteDiagnosticsBootstrap.Initialize();
    public static void Stop() => RemoteDiagnosticsBootstrap.Stop();
}

public sealed class RemoteDiagnosticsModule : IHotUpdateModule
{
    public HotUpdateModuleDescriptor Descriptor { get; } = new()
    {
        ModuleId = "com.bengine.diagnostics.remote",
        Version = "1.0.0"
    };

    public BValueTask StartAsync(BCancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoteDiagnosticsHost.StartFromEnvironment();
        return BValueTask.CompletedTask;
    }

    public BValueTask StopAsync(BCancellationToken cancellationToken = default)
    {
        RemoteDiagnosticsHost.Stop();
        return BValueTask.CompletedTask;
    }
}

internal static class RemoteDiagnosticsBootstrap
{
    private static RemoteDiagnosticsServer? _server;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    internal static void Initialize()
    {
        if (_server is not null) return;
        var arguments = Environment.GetCommandLineArgs();
        var portText = Value(arguments, "--diagnostics-port") ??
                       Environment.GetEnvironmentVariable("BENGINE_DIAGNOSTICS_PORT");
        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535) return;
        var host = Value(arguments, "--diagnostics-host") ??
                   Environment.GetEnvironmentVariable("BENGINE_DIAGNOSTICS_HOST") ??
                   IPAddress.Loopback.ToString();
        var token = Value(arguments, "--diagnostics-token") ??
                    Environment.GetEnvironmentVariable("BENGINE_DIAGNOSTICS_TOKEN") ?? string.Empty;
        if (!IPAddress.TryParse(host, out var address))
            throw new InvalidOperationException($"Invalid diagnostics bind address '{host}'.");
        _server = RemoteDiagnosticsServer.Start(address, port, token);
        Application.quitting += Stop;
        var loadContext = AssemblyLoadContext.GetLoadContext(typeof(RemoteDiagnosticsBootstrap).Assembly);
        if (loadContext?.IsCollectible == true) loadContext.Unloading += _ => Stop();
        Debug.Log($"REMOTE_DIAGNOSTICS_LISTENING|{_server.EndPoint.Address}:{_server.EndPoint.Port}");
    }

    private static string? Value(string[] arguments, string name)
    {
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (argument.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return argument[(name.Length + 1)..];
            if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase) &&
                index + 1 < arguments.Length)
                return arguments[index + 1];
        }
        return null;
    }

    internal static void Stop()
    {
        Application.quitting -= Stop;
        Interlocked.Exchange(ref _server, null)?.Dispose();
    }
}
