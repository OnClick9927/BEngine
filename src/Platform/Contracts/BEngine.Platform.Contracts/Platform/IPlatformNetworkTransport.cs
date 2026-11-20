using System.Net;
using System.Net.WebSockets;

namespace BEngine.Platform;

/// <summary>Non-blocking host I/O. Completion and callbacks are observed by Poll on the owner thread.</summary>
public enum PlatformNetworkOperationStatus { Running, Succeeded, Failed }

public interface IPlatformNetworkOperation : IDisposable
{
    PlatformNetworkOperationStatus Status { get; }
    Exception? Exception { get; }
    void Poll();
    void Abort();
}

public interface IPlatformNetworkOperation<out T> : IPlatformNetworkOperation
{
    T Result { get; }
}

/// <summary>Request buffers and header collections remain borrowed until the operation is disposed.</summary>
public sealed record PlatformHttpRequest(Uri Uri, string Method,
    IReadOnlyDictionary<string, string> Headers, byte[]? UploadData, string? UploadContentType)
{
    public bool BypassCache { get; init; }
}

public sealed record PlatformHttpResponse(HttpStatusCode StatusCode, string? ReasonPhrase,
    IReadOnlyDictionary<string, string[]> Headers);

public interface IPlatformHttpDownloadHandler
{
    void ReceiveResponse(PlatformHttpResponse response);
    /// <summary>The block is borrowed for this callback; retain a copy if it must outlive the callback.</summary>
    void ReceiveData(ReadOnlyMemory<byte> data);
}

public interface IPlatformHttpOperation : IPlatformNetworkOperation
{
    PlatformHttpResponse? Response { get; }
    long DownloadedBytes { get; }
    long? ContentLength { get; }
}

public interface IPlatformHttpTransport
{
    IPlatformHttpOperation Send(PlatformHttpRequest request, IPlatformHttpDownloadHandler handler);
}

public interface IPlatformSocketTransportFactory
{
    IPlatformTcpTransport CreateTcpTransport();
    IPlatformUdpTransport CreateUdpTransport(IPEndPoint? localEndPoint = null);
}

public interface IPlatformTcpTransport : IDisposable
{
    bool IsConnected { get; }
    EndPoint? LocalEndPoint { get; }
    EndPoint? RemoteEndPoint { get; }
    bool NoDelay { get; set; }
    IPlatformNetworkOperation Connect(string host, int port);
    IPlatformNetworkOperation Send(ReadOnlyMemory<byte> data);
    IPlatformNetworkOperation<int> Receive(Memory<byte> buffer);
    /// <summary>Returns false while not writable. A successful zero count means end of stream.</summary>
    bool TrySend(ReadOnlySpan<byte> data, out int count);
    /// <summary>Returns false while not readable. A successful zero count means end of stream.</summary>
    bool TryReceive(Span<byte> buffer, out int count);
    void Close();
}

public readonly record struct PlatformNetworkDatagram(byte[] Data, IPEndPoint RemoteEndPoint);

public interface IPlatformUdpTransport : IDisposable
{
    IPEndPoint? LocalEndPoint { get; }
    IPEndPoint? RemoteEndPoint { get; }
    void Connect(string host, int port);
    IPlatformNetworkOperation<int> Send(ReadOnlyMemory<byte> data, IPEndPoint? remoteEndPoint = null);
    IPlatformNetworkOperation<PlatformNetworkDatagram> Receive();
    void Close();
}

public readonly record struct PlatformWebSocketFrame(int Count, WebSocketMessageType MessageType,
    bool EndOfMessage, WebSocketCloseStatus? CloseStatus = null, string? CloseStatusDescription = null);

public interface IPlatformWebSocketTransport : IDisposable
{
    WebSocketState State { get; }
    string? SubProtocol { get; }
    WebSocketCloseStatus? CloseStatus { get; }
    string? CloseStatusDescription { get; }
    IPlatformNetworkOperation Connect(Uri uri);
    IPlatformNetworkOperation Send(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage);
    IPlatformNetworkOperation<PlatformWebSocketFrame> Receive(ArraySegment<byte> buffer);
    IPlatformNetworkOperation Close(WebSocketCloseStatus status, string? description);
    void Abort();
}
