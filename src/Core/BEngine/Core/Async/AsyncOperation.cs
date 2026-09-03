namespace BEngine;

public class AsyncOperation
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly BTask _task;
    private TaskCompletionSource _activation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Action<AsyncOperation>? _completed;
    private Action<AsyncOperation>? _progressChanged;
    private AsyncOperationStatus _status = AsyncOperationStatus.Created;
    private Exception? _exception;
    private float _progress;
    private bool _allowSceneActivation = true;
    private int _priority;

    public bool isDone
    {
        get { lock (_gate) return IsTerminal(_status); }
    }

    public float progress
    {
        get { lock (_gate) return _progress; }
    }

    public AsyncOperationStatus status
    {
        get { lock (_gate) return _status; }
    }

    public Exception? exception
    {
        get { lock (_gate) return _exception; }
    }

    public virtual bool allowSceneActivation
    {
        get { lock (_gate) return _allowSceneActivation; }
        set
        {
            var release = false;
            lock (_gate)
            {
                if (IsTerminal(_status)) return;
                if (!value && _allowSceneActivation && _activation.Task.IsCompleted)
                    _activation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _allowSceneActivation = value;
                release = value;
            }
            if (release) _activation.TrySetResult();
        }
    }

    public int priority
    {
        get { lock (_gate) return _priority; }
        set { lock (_gate) _priority = value; }
    }

    public BTask task => _task;

    public event Action<AsyncOperation>? completed
    {
        add
        {
            ArgumentNullException.ThrowIfNull(value);
            var invokeNow = false;
            lock (_gate)
            {
                if (IsTerminal(_status)) invokeNow = true;
                else _completed += value;
            }
            if (invokeNow) InvokeHandler(value);
        }
        remove
        {
            lock (_gate) _completed -= value;
        }
    }

    public event Action<AsyncOperation>? progressChanged
    {
        add
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_gate) _progressChanged += value;
        }
        remove
        {
            lock (_gate) _progressChanged -= value;
        }
    }

    protected AsyncOperation()
    {
        _task = BTask.FromTask(_completion.Task);
        _activation.TrySetResult();
    }

    public bool Cancel()
    {
        lock (_gate)
        {
            if (IsTerminal(_status) || _cancellation.IsCancellationRequested) return false;
            _cancellation.Cancel();
            _activation.TrySetCanceled(_cancellation.Token);
            return true;
        }
    }

    public BTaskAwaiter GetAwaiter() => _task.GetAwaiter();

    protected void Start(Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_gate)
        {
            if (_status != AsyncOperationStatus.Created)
                throw new InvalidOperationException("An AsyncOperation can only be started once.");
            _status = AsyncOperationStatus.Running;
        }
        _ = ExecuteAsync(operation);
    }

    protected void SetProgress(float value)
    {
        Action<AsyncOperation>? handlers;
        lock (_gate)
        {
            if (IsTerminal(_status)) return;
            value = Math.Clamp(value, 0f, 0.999999f);
            if (value <= _progress) return;
            _progress = value;
            handlers = _progressChanged;
        }
        InvokeHandlers(handlers);
    }

    protected async Task WaitForActivationAsync(CancellationToken cancellationToken)
    {
        Task wait;
        lock (_gate)
        {
            if (_allowSceneActivation) return;
            wait = _activation.Task;
        }
        await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> operation)
    {
        try
        {
            await operation(_cancellation.Token).ConfigureAwait(false);
            _cancellation.Token.ThrowIfCancellationRequested();
            Finish(AsyncOperationStatus.Succeeded, null);
        }
        catch (OperationCanceledException exception) when (_cancellation.IsCancellationRequested ||
                                                            exception.CancellationToken.IsCancellationRequested)
        {
            Finish(AsyncOperationStatus.Canceled, exception);
        }
        catch (Exception exception)
        {
            Finish(AsyncOperationStatus.Failed, exception);
        }
    }

    private void Finish(AsyncOperationStatus finalStatus, Exception? failure)
    {
        Action<AsyncOperation>? completedHandlers;
        Action<AsyncOperation>? progressHandlers;
        lock (_gate)
        {
            if (IsTerminal(_status)) return;
            _status = finalStatus;
            _exception = failure;
            if (finalStatus == AsyncOperationStatus.Succeeded) _progress = 1f;
            completedHandlers = _completed;
            progressHandlers = _progressChanged;
            _completed = null;
            _progressChanged = null;
            switch (finalStatus)
            {
                case AsyncOperationStatus.Succeeded:
                    _completion.TrySetResult();
                    break;
                case AsyncOperationStatus.Canceled:
                    _completion.TrySetCanceled(_cancellation.Token);
                    break;
                case AsyncOperationStatus.Failed:
                    _completion.TrySetException(failure!);
                    break;
            }
        }

        InvokeHandlers(progressHandlers);
        InvokeHandlers(completedHandlers);
    }

    private void InvokeHandlers(Action<AsyncOperation>? handlers)
    {
        if (handlers is null) return;
        foreach (Action<AsyncOperation> handler in handlers.GetInvocationList()) InvokeHandler(handler);
    }

    private void InvokeHandler(Action<AsyncOperation> handler)
    {
        try { handler(this); }
        catch (Exception callbackException)
        {
            Debug.LogException(callbackException);
        }
    }

    private static bool IsTerminal(AsyncOperationStatus value) =>
        value is AsyncOperationStatus.Succeeded or AsyncOperationStatus.Failed or AsyncOperationStatus.Canceled;
}
