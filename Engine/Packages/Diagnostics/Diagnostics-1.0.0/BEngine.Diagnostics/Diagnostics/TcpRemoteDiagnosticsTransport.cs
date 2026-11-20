using BEngine.Instrumentation;
using BEngine.Diagnostics;
using System.Net;
using BEngine.Platform;

using System.Text;
using System.Text.Json;

using BEngine.Rendering.Rhi;

namespace BEngine.Platform.DotNet.Diagnostics;

public sealed class TcpRemoteDiagnosticsServer : IRemoteDiagnosticsServer
{
    public const int ProtocolVersion = RemoteDiagnosticsProtocol.Version;
    public const int MaximumMessageBytes = RemoteDiagnosticsProtocol.MaximumMessageBytes;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IPlatformTcpStreamListener _listener;
    private readonly string _token;
    private readonly CancellationTokenSource _stop = new();
    private readonly BTask _acceptLoop;
    private readonly HashSet<IPlatformTcpStreamConnection> _clients = [];
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly IDisposable _gpuTimingLease;
    private int _disposed;

    private TcpRemoteDiagnosticsServer(IPlatformTcpStreamListener listener, string token)
    {
        _listener = listener;
        _token = token;
        _ = RuntimeDiagnosticsSample.Capture();
        _gpuTimingLease = GraphicsFrameTimingService.Acquire();
        _acceptLoop = AcceptLoopAsync();
    }

    public RemoteDiagnosticsEndpoint EndPoint
    {
        get { var endpoint = _listener.LocalEndPoint; return new(endpoint.Address.ToString(), endpoint.Port); }
    }

    public static TcpRemoteDiagnosticsServer Start(
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
        var listener = RuntimeServices.GetRequiredService<IPlatformTcpStreamFactory>()
            .Listen(new IPEndPoint(address, port), 16);
        try { return new TcpRemoteDiagnosticsServer(listener, token); }
        catch { listener.Dispose(); throw; }
    }

    private async BTask AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            await BTask.Yield();
            if (_stop.IsCancellationRequested) break;
            if (_clients.Count >= 16) continue;
            IPlatformTcpStreamConnection client;
            try { client = await OnFrame(_listener.AcceptAsync(_stop.Token)); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            if (_stop.IsCancellationRequested) { client.Dispose(); break; }
            _clients.Add(client);
            _ = HandleClientAsync(client);
        }
    }

    private async BTask HandleClientAsync(IPlatformTcpStreamConnection client)
    {
        try
        {
        using (client)
        using (var stream = client.Stream)
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
                    await WriteAsync(writer, RemoteDiagnosticsResponse.Failure("Message exceeds the protocol limit."));
                    break;
                }
                RemoteDiagnosticsRequest? request;
                try { request = JsonSerializer.Deserialize<RemoteDiagnosticsRequest>(line, JsonOptions); }
                catch (JsonException exception)
                {
                    await WriteAsync(writer, RemoteDiagnosticsResponse.Failure(exception.Message));
                    continue;
                }
                if (request is null || request.Protocol != ProtocolVersion)
                {
                    await WriteAsync(writer, RemoteDiagnosticsResponse.Failure(
                        $"Diagnostics protocol {ProtocolVersion} is required."));
                    continue;
                }
                if (!FixedTimeEquals(request.Token ?? string.Empty, _token))
                {
                    await WriteAsync(writer, RemoteDiagnosticsResponse.Failure("Authentication failed."));
                    break;
                }
                if (Environment.CurrentManagedThreadId != _ownerThread)
                    throw new InvalidOperationException("Diagnostics capture must run on its owning engine thread.");
                var response = request.Operation switch
                {
                    "hello" => RemoteDiagnosticsResponse.Success(new RemoteHello(
                        ProtocolVersion, Environment.ProcessId, Environment.MachineName)),
                    "sample" => RemoteDiagnosticsResponse.Success(RuntimeDiagnosticsSample.Capture()),
                    "heap" => RemoteDiagnosticsResponse.Success(EngineObjectHeapSnapshot.Capture(
                        request.ForceGarbageCollection)),
                    _ => RemoteDiagnosticsResponse.Failure($"Unknown operation '{request.Operation}'.")
                };
                await WriteAsync(writer, response);
            }
        }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally { _clients.Remove(client); }
    }

    private static async BTask WriteAsync(StreamWriter writer, RemoteDiagnosticsResponse response)
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
        _listener.Dispose();
        foreach (var client in _clients.ToArray()) client.Dispose();
        _clients.Clear();
        _gpuTimingLease.Dispose();
        _stop.Dispose();
    }

}

public sealed class TcpRemoteDiagnosticsClient : IRemoteDiagnosticsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IPlatformTcpStreamConnection _client;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly string _token;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private int _disposed;

    private TcpRemoteDiagnosticsClient(IPlatformTcpStreamConnection client, string token)
    {
        _client = client;
        _token = token;
        var stream = client.Stream;
        _reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
        _writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true)
            { AutoFlush = true };
    }

    public RemoteHello Hello { get; private set; } = new(0, 0, string.Empty);

    public static async Task<TcpRemoteDiagnosticsClient> ConnectAsync(
        string host,
        int port,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("TCP diagnostics is not supported in the browser.");
        var client = await RuntimeServices.GetRequiredService<IPlatformTcpStreamFactory>()
            .ConnectAsync(host.Trim(), port, noDelay: true, cancellationToken).ConfigureAwait(false);
        TcpRemoteDiagnosticsClient? result = null;
        try
        {
            result = new TcpRemoteDiagnosticsClient(client, token?.Trim() ?? string.Empty);
            result.Hello = await result.RequestAsync<RemoteHello>(
                "hello", false, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            if (result is not null) await result.DisposeAsync().ConfigureAwait(false);
            else client.Dispose();
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
                protocol = TcpRemoteDiagnosticsServer.ProtocolVersion,
                operation,
                token = _token,
                forceGarbageCollection
            };
            await _writer.WriteLineAsync(
                JsonSerializer.Serialize(request, JsonOptions).AsMemory(),
                cancellationToken).ConfigureAwait(false);
            var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) ??
                       throw new EndOfStreamException("Remote diagnostics connection closed.");
            if (Encoding.UTF8.GetByteCount(line) > TcpRemoteDiagnosticsServer.MaximumMessageBytes)
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

