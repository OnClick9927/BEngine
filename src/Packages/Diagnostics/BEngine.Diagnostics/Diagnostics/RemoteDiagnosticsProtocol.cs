using System.Text.Json;

namespace BEngine.Diagnostics;

public static class RemoteDiagnosticsProtocol
{
    public const int Version = 1;
    public const int MaximumMessageBytes = 16 * 1024 * 1024;
}

public sealed record RemoteHello(int Protocol, int ProcessId, string MachineName);
public sealed record RemoteDiagnosticsEndpoint(string Address, int Port);

public sealed record RemoteDiagnosticsRequest(int Protocol, string Operation, string Token,
    bool ForceGarbageCollection = false);

public sealed record RemoteDiagnosticsResponse(bool Ok, string Error, JsonElement Data)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static RemoteDiagnosticsResponse Success<T>(T value) =>
        new(true, string.Empty, JsonSerializer.SerializeToElement(value, JsonOptions));
    public static RemoteDiagnosticsResponse Failure(string error) =>
        new(false, error, JsonSerializer.SerializeToElement<object?>(null, JsonOptions));
}

public interface IRemoteDiagnosticsServer : IDisposable
{
    RemoteDiagnosticsEndpoint EndPoint { get; }
}

public interface IRemoteDiagnosticsClient : IAsyncDisposable
{
    RemoteHello Hello { get; }
    Task<RuntimeDiagnosticsSample> CaptureSampleAsync(CancellationToken cancellationToken = default);
    Task<EngineObjectHeapSnapshot> CaptureHeapAsync(bool forceGarbageCollection = false, CancellationToken cancellationToken = default);
}

/// <summary>Diagnostic protocol and sampling workflow; native byte streams are supplied by the host.</summary>
public interface IRemoteDiagnosticsTransport
{
    IRemoteDiagnosticsServer Start(string bindAddress, int port, string token);
    Task<IRemoteDiagnosticsClient> ConnectAsync(string host, int port, string token, CancellationToken cancellationToken = default);
}

public static class RemoteDiagnosticsServer
{
    public const int ProtocolVersion = RemoteDiagnosticsProtocol.Version;
    public const int MaximumMessageBytes = RemoteDiagnosticsProtocol.MaximumMessageBytes;
    public static IRemoteDiagnosticsServer Start(string bindAddress, int port, string token) =>
        RuntimeServices.GetRequiredService<IRemoteDiagnosticsTransport>().Start(bindAddress, port, token);
}

public static class RemoteDiagnosticsClient
{
    public static Task<IRemoteDiagnosticsClient> ConnectAsync(string host, int port, string token,
        CancellationToken cancellationToken = default) =>
        RuntimeServices.GetRequiredService<IRemoteDiagnosticsTransport>().ConnectAsync(host, port, token, cancellationToken);
}
