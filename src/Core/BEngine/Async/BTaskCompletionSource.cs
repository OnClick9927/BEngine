namespace BEngine;

public sealed class BTaskCompletionSource : BTask
{
    public BTaskCompletionSource() : this(BTaskScheduler.Main)
    {
    }

    internal BTaskCompletionSource(BTaskScheduler scheduler) : base(scheduler)
    {
    }

    public BTask Task => this;
    public void SetResult()
    {
        if (!TrySetResult()) throw AlreadyCompleted();
    }

    public void SetException(Exception exception)
    {
        if (!TrySetException(exception)) throw AlreadyCompleted();
    }

    public void SetCanceled(BCancellationToken cancellationToken = default)
    {
        if (!TrySetCanceled(cancellationToken)) throw AlreadyCompleted();
    }

    public new bool TrySetResult() => base.TrySetResult();
    public new bool TrySetException(Exception exception) => base.TrySetException(exception);
    public new bool TrySetCanceled(BCancellationToken cancellationToken = default) =>
        base.TrySetCanceled(cancellationToken);

    private static InvalidOperationException AlreadyCompleted() =>
        new("The BTask completion source has already completed.");
}

public sealed class BTaskCompletionSource<TResult> : BTask<TResult>
{
    public BTaskCompletionSource() : this(BTaskScheduler.Main)
    {
    }

    internal BTaskCompletionSource(BTaskScheduler scheduler) : base(scheduler)
    {
    }

    public BTask<TResult> Task => this;
    public void SetResult(TResult result)
    {
        if (!TrySetResult(result)) throw AlreadyCompleted();
    }

    public void SetException(Exception exception)
    {
        if (!TrySetException(exception)) throw AlreadyCompleted();
    }

    public void SetCanceled(BCancellationToken cancellationToken = default)
    {
        if (!TrySetCanceled(cancellationToken)) throw AlreadyCompleted();
    }

    public new bool TrySetResult(TResult result) => base.TrySetResult(result);
    public new bool TrySetException(Exception exception) => base.TrySetException(exception);
    public new bool TrySetCanceled(BCancellationToken cancellationToken = default) =>
        base.TrySetCanceled(cancellationToken);

    private static InvalidOperationException AlreadyCompleted() =>
        new("The BTask completion source has already completed.");
}
