namespace BEngine.Networking;

public enum NetworkRequestResult
{
    NotStarted,
    InProgress,
    Success,
    ConnectionError,
    ProtocolError,
    DataProcessingError,
    Canceled,
    TimedOut
}
