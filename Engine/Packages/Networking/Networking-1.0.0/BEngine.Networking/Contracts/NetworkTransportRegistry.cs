namespace BEngine.Networking;

/// <summary>
/// Platform hosts install transports during startup. Registration and use are
/// both part of the engine frame lifecycle.
/// </summary>
public static class NetworkTransportRegistry
{
    public static IHttpTransport? HttpTransport { get; set; }
    public static Func<IWebSocketTransport>? WebSocketTransportFactory { get; set; }
    public static ISocketTransportFactory? SocketTransportFactory { get; set; }
    internal static ISocketTransportFactory Sockets => SocketTransportFactory ??
        RuntimeServiceAccess.GetRequiredService<ISocketTransportFactory>();

    internal static IWebSocketTransport CreateWebSocketTransport() =>
        WebSocketTransportFactory?.Invoke() ??
        RuntimeServiceAccess.GetService<Func<IWebSocketTransport>>()?.Invoke() ??
        throw new PlatformNotSupportedException(
            "The active player host has not registered a WebSocket transport.");
}
