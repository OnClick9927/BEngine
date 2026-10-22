using System.Net.Sockets;
using System.Net.WebSockets;

namespace BEngine.Networking;

public abstract class NetworkClientBase : IDisposable
{
    public static readonly TimeSpan InfiniteTimeout = TimeSpan.FromMilliseconds(-1);

    private bool _disposed;
    private bool _closed;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public NetworkRequestResult LastResult { get; private set; } = NetworkRequestResult.NotStarted;
    public string? Error { get; private set; }
    public Exception? Exception { get; private set; }
    public bool IsDisposed => _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public BTask DisposeAsync()
    {
        Dispose();
        return BTask.CompletedTask;
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    protected async BTask RunOperationAsync(
        INetworkOperation operation,
        BCancellationToken cancellationToken = default)
    {
        await RunOperationAsync(new ResultlessOperation(operation), cancellationToken);
    }

    protected async BTask<TResult> RunOperationAsync<TResult>(
        INetworkOperation<TResult> operation,
        BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ThrowIfDisposed();
        ValidateTimeout();
        LastResult = NetworkRequestResult.InProgress;
        Error = null;
        Exception = null;
        var startedAt = BTaskScheduler.Main.Elapsed;

        using (operation)
        {
            try
            {
                await BTask.Yield();
                while (true)
                {
                    ThrowIfStopped(cancellationToken, startedAt, operation);
                    if (operation.Status != NetworkOperationStatus.Running) break;
                    operation.Poll();
                    ThrowIfStopped(cancellationToken, startedAt, operation);
                    if (operation.Status == NetworkOperationStatus.Running)
                        await BTask.Yield();
                }

                if (operation.Status == NetworkOperationStatus.Failed)
                    throw operation.Exception ??
                          new IOException("The platform network operation failed without an error.");

                LastResult = NetworkRequestResult.Success;
                return operation.Result;
            }
            catch (TimeoutException exception)
            {
                RecordFailure(NetworkRequestResult.TimedOut, exception.Message, exception);
                throw;
            }
            catch (BOperationCanceledException exception)
            {
                operation.Abort();
                RecordFailure(NetworkRequestResult.Canceled,
                    "The network operation was canceled.", exception);
                throw;
            }
            catch (InvalidDataException exception)
            {
                operation.Abort();
                RecordFailure(NetworkRequestResult.DataProcessingError, exception.Message, exception);
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or SocketException or WebSocketException)
            {
                operation.Abort();
                RecordFailure(NetworkRequestResult.ConnectionError, exception.Message, exception);
                throw;
            }
            catch (Exception exception)
            {
                operation.Abort();
                RecordFailure(NetworkRequestResult.DataProcessingError, exception.Message, exception);
                throw;
            }
        }
    }

    protected void SetClosedResult()
    {
        _closed = true;
        LastResult = NetworkRequestResult.Success;
        Error = null;
        Exception = null;
    }

    private void ThrowIfStopped(
        BCancellationToken cancellationToken,
        TimeSpan startedAt,
        INetworkOperation operation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || _closed)
            throw new BOperationCanceledException(BCancellationToken.None);
        if (Timeout == InfiniteTimeout || BTaskScheduler.Main.Elapsed - startedAt < Timeout) return;
        operation.Abort();
        throw new TimeoutException($"The network operation timed out after {Timeout}.");
    }

    private void ValidateTimeout()
    {
        if (Timeout <= TimeSpan.Zero && Timeout != InfiniteTimeout)
            throw new InvalidOperationException(
                $"Timeout must be positive or {nameof(InfiniteTimeout)}.");
    }

    private void RecordFailure(NetworkRequestResult result, string error, Exception exception)
    {
        LastResult = result;
        Error = error;
        Exception = exception;
    }

    private sealed class ResultlessOperation(INetworkOperation operation) : INetworkOperation<bool>
    {
        public NetworkOperationStatus Status => operation.Status;
        public Exception? Exception => operation.Exception;
        public bool Result => true;
        public void Poll() => operation.Poll();
        public void Abort() => operation.Abort();
        public void Dispose() => operation.Dispose();
    }
}
