namespace BEngine;

public class AsyncOperation
{
    private readonly BCancellationSource _cancellation = new();
    private readonly BTaskCompletionSource _completion = new();
    private BTaskCompletionSource _activation = new();
    private Action<AsyncOperation>? _completed;
    private AsyncOperationStatus _status = AsyncOperationStatus.Created;
    private Exception? _exception;
    private float _progress;
    private bool _allowSceneActivation = true;
    private int _priority;
    private AsyncLoadContext? _loadContext;
    protected AsyncLoadContext LoadContext => _loadContext ??= new(_cancellation.Token, SetProgress) { priority = _priority };

    public bool isDone => IsTerminal(_status);
    public float progress => _progress;
    public AsyncOperationStatus status => _status;
    public Exception? exception => _exception;

    public virtual bool allowSceneActivation
    {
        get => _allowSceneActivation;
        set
        {
            if (IsTerminal(_status) || value == _allowSceneActivation) return;
            _allowSceneActivation = value;
            if (value)
            {
                _activation.TrySetResult();
            }
            else if (_activation.IsCompleted)
            {
                _activation = new BTaskCompletionSource();
            }
        }
    }

    public int priority
    {
        get => _priority;
        set { _priority = value; if (_loadContext is not null) _loadContext.priority = value; }
    }

    public BTask task => _completion.Task;

    public event Action<AsyncOperation>? completed
    {
        add
        {
            ArgumentNullException.ThrowIfNull(value);
            if (IsTerminal(_status)) InvokeHandler(value);
            else _completed += value;
        }
        remove => _completed -= value;
    }

    protected AsyncOperation()
    {
        _activation.SetResult();
    }

    public bool Cancel()
    {
        if (IsTerminal(_status) || !_cancellation.Cancel()) return false;
        _activation.TrySetCanceled(_cancellation.Token);
        return true;
    }

    public BTaskAwaiter GetAwaiter() => task.GetAwaiter();

    protected void Start(Func<BCancellationToken, BTask> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (_status != AsyncOperationStatus.Created)
            throw new InvalidOperationException("An AsyncOperation can only be started once.");
        _status = AsyncOperationStatus.Running;
        _ = ExecuteAsync(operation);
    }

    protected void SetProgress(float value)
    {
        if (IsTerminal(_status)) return;
        value = Math.Clamp(value, 0f, 0.999999f);
        if (value <= _progress) return;
        _progress = value;
    }

    protected BValueTask WaitForActivationAsync(BCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _allowSceneActivation
            ? BValueTask.CompletedTask
            : new BValueTask(_activation.Task);
    }

    private async BTask ExecuteAsync(Func<BCancellationToken, BTask> operation)
    {
        try
        {
            await operation(_cancellation.Token);
            _cancellation.Token.ThrowIfCancellationRequested();
            Finish(AsyncOperationStatus.Succeeded, null, default);
        }
        catch (BOperationCanceledException exception)
        {
            Finish(AsyncOperationStatus.Canceled, exception, exception.CancellationToken);
        }
        catch (Exception exception)
        {
            Finish(AsyncOperationStatus.Failed, exception, default);
        }
    }

    private void Finish(
        AsyncOperationStatus finalStatus,
        Exception? failure,
        BCancellationToken cancellationToken)
    {
        if (IsTerminal(_status)) return;
        _status = finalStatus;
        _exception = failure;
        if (finalStatus == AsyncOperationStatus.Succeeded) _progress = 1f;

        switch (finalStatus)
        {
            case AsyncOperationStatus.Succeeded:
                _completion.TrySetResult();
                break;
            case AsyncOperationStatus.Canceled:
                _completion.TrySetCanceled(cancellationToken);
                break;
            case AsyncOperationStatus.Failed:
                _completion.TrySetException(failure!);
                break;
        }

        var completedHandlers = _completed;
        _completed = null;
        InvokeHandlers(completedHandlers);
    }

    private void InvokeHandlers(Action<AsyncOperation>? handlers)
    {
        if (handlers is null) return;
        foreach (Action<AsyncOperation> handler in Delegate.EnumerateInvocationList(handlers)) InvokeHandler(handler);
    }

    private void InvokeHandler(Action<AsyncOperation> handler)
    {
        try
        {
            handler(this);
        }
        catch (Exception callbackException)
        {
            Debug.LogException(callbackException);
        }
    }

    private static bool IsTerminal(AsyncOperationStatus value) =>
        value is AsyncOperationStatus.Succeeded or AsyncOperationStatus.Failed or AsyncOperationStatus.Canceled;
}
