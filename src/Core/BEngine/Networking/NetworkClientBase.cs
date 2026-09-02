namespace BEngine.Networking;

public abstract class NetworkClientBase : IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public NetworkRequestResult LastResult { get; private set; } = NetworkRequestResult.NotStarted;
    public string? Error { get; private set; }
    public Exception? Exception { get; private set; }
    public bool IsDisposed => _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        Dispose(true);
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(true);
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    protected virtual ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;

    protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    protected async Task RunAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        await RunAsync(async token =>
        {
            await operation(token).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    protected async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidateTimeout();
        LastResult = NetworkRequestResult.InProgress;
        Error = null;
        Exception = null;

        using var timeout = CreateTimeoutSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.Token, timeout.Token);
        try
        {
            var value = await operation(linked.Token).ConfigureAwait(false);
            LastResult = NetworkRequestResult.Success;
            return value;
        }
        catch (OperationCanceledException exception) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested &&
            !_lifetime.IsCancellationRequested)
        {
            LastResult = NetworkRequestResult.TimedOut;
            Error = $"The network operation timed out after {Timeout}.";
            Exception = exception;
            throw new TimeoutException(Error, exception);
        }
        catch (OperationCanceledException exception)
        {
            LastResult = NetworkRequestResult.Canceled;
            Error = "The network operation was canceled.";
            Exception = exception;
            throw;
        }
        catch (InvalidDataException exception)
        {
            LastResult = NetworkRequestResult.DataProcessingError;
            Error = exception.Message;
            Exception = exception;
            throw;
        }
        catch (Exception exception) when (exception is IOException or System.Net.Sockets.SocketException or
                                          System.Net.WebSockets.WebSocketException or HttpRequestException)
        {
            LastResult = NetworkRequestResult.ConnectionError;
            Error = exception.Message;
            Exception = exception;
            throw;
        }
        catch (Exception exception)
        {
            LastResult = NetworkRequestResult.DataProcessingError;
            Error = exception.Message;
            Exception = exception;
            throw;
        }
    }

    protected void SetClosedResult()
    {
        LastResult = NetworkRequestResult.Success;
        Error = null;
        Exception = null;
    }

    private CancellationTokenSource CreateTimeoutSource()
    {
        var source = new CancellationTokenSource();
        if (Timeout != System.Threading.Timeout.InfiniteTimeSpan)
            source.CancelAfter(Timeout);
        return source;
    }

    private void ValidateTimeout()
    {
        if (Timeout <= TimeSpan.Zero && Timeout != System.Threading.Timeout.InfiniteTimeSpan)
            throw new InvalidOperationException("Timeout must be positive or Timeout.InfiniteTimeSpan.");
    }
}
