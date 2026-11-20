using BEngine.Platform;
using System.Net.WebSockets;

namespace BEngine.Networking;

public sealed class PlatformWebSocketTransport : IWebSocketTransport
{
    private readonly IPlatformWebSocketTransport _native =
        (PlatformRuntime.GetService<Func<IPlatformWebSocketTransport>>() ??
            throw new PlatformNotSupportedException("The host has no WebSocket transport."))();
    public WebSocketState State => _native.State;
    public string? SubProtocol => _native.SubProtocol;
    public WebSocketCloseStatus? CloseStatus => _native.CloseStatus;
    public string? CloseStatusDescription => _native.CloseStatusDescription;
    public INetworkOperation Connect(Uri uri) => new PlatformNetworkOperation(_native.Connect(uri));
    public INetworkOperation Send(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage) =>
        new PlatformNetworkOperation(_native.Send(buffer, messageType, endOfMessage));
    public INetworkOperation<WebSocketFrame> Receive(ArraySegment<byte> buffer) =>
        new PlatformMappedNetworkOperation<PlatformWebSocketFrame, WebSocketFrame>(_native.Receive(buffer),
            static frame => new WebSocketFrame(frame.Count, frame.MessageType, frame.EndOfMessage,
                frame.CloseStatus, frame.CloseStatusDescription));
    public INetworkOperation Close(WebSocketCloseStatus closeStatus, string? statusDescription) =>
        new PlatformNetworkOperation(_native.Close(closeStatus, statusDescription));
    public void Abort() => _native.Abort();
    public void Dispose() => _native.Dispose();
}
