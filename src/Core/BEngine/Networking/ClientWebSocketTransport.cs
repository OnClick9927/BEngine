using System.Net.WebSockets;

namespace BEngine.Networking;

public sealed class ClientWebSocketTransport : IWebSocketTransport
{
    private readonly ClientWebSocket _socket = new();

    public WebSocketState State => _socket.State;
    public string? SubProtocol => _socket.SubProtocol;
    public WebSocketCloseStatus? CloseStatus => _socket.CloseStatus;
    public string? CloseStatusDescription => _socket.CloseStatusDescription;
    public ClientWebSocketOptions Options => _socket.Options;

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
        _socket.ConnectAsync(uri, cancellationToken);

    public Task SendAsync(
        ArraySegment<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken) =>
        _socket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);

    public Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken) =>
        _socket.ReceiveAsync(buffer, cancellationToken);

    public Task CloseAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken) =>
        _socket.CloseAsync(closeStatus, statusDescription, cancellationToken);

    public void Abort() => _socket.Abort();
    public void Dispose() => _socket.Dispose();
}
