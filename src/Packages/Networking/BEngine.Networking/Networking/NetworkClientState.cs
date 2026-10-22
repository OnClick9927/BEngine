namespace BEngine.Networking;

public enum NetworkClientState
{
    Disconnected,
    Connecting,
    Connected,
    Closing,
    Closed,
    Faulted
}
