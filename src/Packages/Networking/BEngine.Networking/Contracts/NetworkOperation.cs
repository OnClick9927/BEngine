namespace BEngine.Networking;

public enum NetworkOperationStatus
{
    Running,
    Succeeded,
    Failed
}

/// <summary>
/// A platform or socket operation advanced once per engine frame.
/// Implementations must never block while polling.
/// </summary>
public interface INetworkOperation : IDisposable
{
    NetworkOperationStatus Status { get; }
    Exception? Exception { get; }
    void Poll();
    void Abort();
}

public interface INetworkOperation<out TResult> : INetworkOperation
{
    TResult Result { get; }
}
