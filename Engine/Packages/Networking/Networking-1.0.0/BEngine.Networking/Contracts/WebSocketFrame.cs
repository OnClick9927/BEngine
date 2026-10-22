using System.Net.WebSockets;

namespace BEngine.Networking;

public readonly record struct WebSocketFrame(
    int Count,
    WebSocketMessageType MessageType,
    bool EndOfMessage,
    WebSocketCloseStatus? CloseStatus = null,
    string? CloseStatusDescription = null);
