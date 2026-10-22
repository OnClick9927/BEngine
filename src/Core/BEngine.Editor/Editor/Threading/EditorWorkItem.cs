namespace BEngine.Editor;

internal sealed class EditorWorkItem<T> : IEditorWorkItem
{
    private readonly Func<CancellationToken, ValueTask<T>> _operation;
    private readonly CancellationToken _cancellationToken;
    private readonly TaskCompletionSource<T> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public EditorWorkItem(
        string name,
        Func<CancellationToken, ValueTask<T>> operation,
        EditorTaskPriority priority,
        CancellationToken cancellationToken)
    {
        Name = name;
        Priority = priority;
        _operation = operation;
        _cancellationToken = cancellationToken;
    }

    public string Name { get; }
    public EditorTaskPriority Priority { get; }
    public Task<T> Task => _completion.Task;
    Task IEditorWorkItem.Completion => _completion.Task;

    public async ValueTask ExecuteAsync(CancellationToken shutdownToken)
    {
        if (_cancellationToken.IsCancellationRequested)
        {
            _completion.TrySetCanceled(_cancellationToken);
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            _cancellationToken, shutdownToken);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            var result = await _operation(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            _completion.TrySetResult(result);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            _completion.TrySetCanceled(linked.Token);
        }
        catch (Exception exception)
        {
            _completion.TrySetException(exception);
        }
    }

    public void Cancel(CancellationToken cancellationToken) =>
        _completion.TrySetCanceled(cancellationToken);
}
