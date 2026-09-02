using System.Net.WebSockets;
using System.Text;

namespace BEngine.Networking;

public sealed record WebSocketMessage(
    WebSocketMessageType MessageType,
    byte[] Data,
    WebSocketCloseStatus? CloseStatus = null,
    string? CloseStatusDescription = null)
{
    public string Text => Encoding.UTF8.GetString(Data);
    public bool IsClose => MessageType == WebSocketMessageType.Close;
}
