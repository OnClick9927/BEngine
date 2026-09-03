namespace BEngine;

/// <summary>
/// Producer side of a manually completed <see cref="BTask"/>.
/// Continuations always run asynchronously to avoid re-entrancy at completion sites.
/// </summary>
public sealed class BTaskCompletionSource
{
    private readonly TaskCompletionSource _source =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly BTask _task;

    public BTaskCompletionSource() => _task = BTask.FromTask(_source.Task);

    public BTask Task => _task;

    public void SetResult() => _source.SetResult();
    public bool TrySetResult() => _source.TrySetResult();
    public void SetException(Exception exception) =>
        _source.SetException(exception ?? throw new ArgumentNullException(nameof(exception)));
    public bool TrySetException(Exception exception) =>
        _source.TrySetException(exception ?? throw new ArgumentNullException(nameof(exception)));
    public void SetCanceled(CancellationToken cancellationToken = default) =>
        _source.SetCanceled(cancellationToken);
    public bool TrySetCanceled(CancellationToken cancellationToken = default) =>
        _source.TrySetCanceled(cancellationToken);
}

public sealed class BTaskCompletionSource<TResult>
{
    private readonly TaskCompletionSource<TResult> _source =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly BTask<TResult> _task;

    public BTaskCompletionSource() => _task = BTask<TResult>.FromTask(_source.Task);

    public BTask<TResult> Task => _task;

    public void SetResult(TResult result) => _source.SetResult(result);
    public bool TrySetResult(TResult result) => _source.TrySetResult(result);
    public void SetException(Exception exception) =>
        _source.SetException(exception ?? throw new ArgumentNullException(nameof(exception)));
    public bool TrySetException(Exception exception) =>
        _source.TrySetException(exception ?? throw new ArgumentNullException(nameof(exception)));
    public void SetCanceled(CancellationToken cancellationToken = default) =>
        _source.SetCanceled(cancellationToken);
    public bool TrySetCanceled(CancellationToken cancellationToken = default) =>
        _source.TrySetCanceled(cancellationToken);
}
