using System.Net.WebSockets;

namespace BEngine.Networking;

public interface IWebSocketTransport : IDisposable
{
    WebSocketState State { get; }
    string? SubProtocol { get; }
    WebSocketCloseStatus? CloseStatus { get; }
    string? CloseStatusDescription { get; }

    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);
    Task SendAsync(
        ArraySegment<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken);
    Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken);
    Task CloseAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken);
    void Abort();
}
