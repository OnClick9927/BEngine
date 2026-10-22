namespace BEngine.Reactive;

public abstract class BObserver<T> : IDisposable
{
    private IDisposable? _upstream;
    private int _trackingId;
    private bool _disposed;

    public bool IsDisposed => _disposed;

    public void OnNext(T value)
    {
        if (!_disposed) OnNextCore(value);
    }

    public void OnErrorResume(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (_disposed) return;
        try { OnErrorResumeCore(error); }
        catch (Exception exception) { BReactiveSystem.ReportUnhandledException(exception); }
    }

    public void OnCompleted() => OnCompleted(BResult.Success);

    public void OnCompleted(Exception error) => OnCompleted(BResult.Failure(error));

    public void OnCompleted(BResult result)
    {
        if (_disposed) return;
        _disposed = true;
        try { OnCompletedCore(result); }
        finally { Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { DisposeCore(); }
        finally { Release(); }
    }

    protected abstract void OnNextCore(T value);
    protected virtual void OnErrorResumeCore(Exception error) =>
        BReactiveSystem.ReportUnhandledException(error);
    protected virtual void OnCompletedCore(BResult result)
    {
        if (result.IsFailure) BReactiveSystem.ReportUnhandledException(result.Exception!);
    }
    protected virtual void DisposeCore() { }

    internal void SetUpstream(IDisposable upstream)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        if (_disposed)
        {
            upstream.Dispose();
            return;
        }
        if (_upstream is not null)
            throw new InvalidOperationException("An observer can only be connected once.");
        _upstream = upstream;
    }

    internal void SetTrackingId(int trackingId) => _trackingId = trackingId;

    private void Release()
    {
        var upstream = _upstream;
        _upstream = null;
        upstream?.Dispose();
        if (_trackingId == 0) return;
        BObservableTracker.Remove(_trackingId);
        _trackingId = 0;
    }
}
