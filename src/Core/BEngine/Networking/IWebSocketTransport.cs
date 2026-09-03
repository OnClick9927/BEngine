using System.Net.WebSockets;

namespace BEngine.Networking;

public interface IWebSocketTransport : IDisposable
{
    WebSocketState State { get; }
    string? SubProtocol { get; }
    WebSocketCloseStatus? CloseStatus { get; }
    string? CloseStatusDescription { get; }

    BValueTask ConnectAsync(Uri uri, CancellationToken cancellationToken);
    BValueTask SendAsync(
        ArraySegment<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken);
    BValueTask<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken);
    BValueTask CloseAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken);
    void Abort();
}
