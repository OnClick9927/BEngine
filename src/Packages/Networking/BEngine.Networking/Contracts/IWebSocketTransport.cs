using System.Net.WebSockets;

namespace BEngine.Networking;

public interface IWebSocketTransport : IDisposable
{
    WebSocketState State { get; }
    string? SubProtocol { get; }
    WebSocketCloseStatus? CloseStatus { get; }
    string? CloseStatusDescription { get; }

    INetworkOperation Connect(Uri uri);
    INetworkOperation Send(
        ArraySegment<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage);
    INetworkOperation<WebSocketFrame> Receive(ArraySegment<byte> buffer);
    INetworkOperation Close(
        WebSocketCloseStatus closeStatus,
        string? statusDescription);
    void Abort();
}
