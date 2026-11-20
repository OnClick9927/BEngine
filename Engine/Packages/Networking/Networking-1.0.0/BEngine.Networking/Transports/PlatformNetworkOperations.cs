using BEngine.Platform;

namespace BEngine.Networking;

internal class PlatformNetworkOperation(IPlatformNetworkOperation operation) : INetworkOperation
{
    public NetworkOperationStatus Status => operation.Status switch
    {
        PlatformNetworkOperationStatus.Running => NetworkOperationStatus.Running,
        PlatformNetworkOperationStatus.Succeeded => NetworkOperationStatus.Succeeded,
        PlatformNetworkOperationStatus.Failed => NetworkOperationStatus.Failed,
        _ => throw new InvalidOperationException("The host returned an unknown network operation status.")
    };
    public Exception? Exception => operation.Exception;
    public void Poll() => operation.Poll();
    public void Abort() => operation.Abort();
    public void Dispose() => operation.Dispose();
}

internal sealed class PlatformNetworkOperation<T>(IPlatformNetworkOperation<T> operation)
    : PlatformNetworkOperation(operation), INetworkOperation<T>
{
    public T Result => operation.Result;
}

internal sealed class PlatformMappedNetworkOperation<TSource, TResult>(
    IPlatformNetworkOperation<TSource> operation, Func<TSource, TResult> map)
    : PlatformNetworkOperation(operation), INetworkOperation<TResult>
{
    private TResult? _result;
    private bool _mapped;
    public TResult Result
    {
        get
        {
            if (Status != NetworkOperationStatus.Succeeded)
                throw new InvalidOperationException("The host operation has not succeeded.");
            if (!_mapped) { _result = map(operation.Result); _mapped = true; }
            return _result!;
        }
    }
}
