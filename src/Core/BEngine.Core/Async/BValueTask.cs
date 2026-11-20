using System.Runtime.CompilerServices;

namespace BEngine;

[AsyncMethodBuilder(typeof(BValueTaskMethodBuilder))]
public readonly struct BValueTask
{
    private readonly BTask? _source;

    public BValueTask(BTask source) =>
        _source = source ?? throw new ArgumentNullException(nameof(source));

    public static BValueTask CompletedTask => default;
    public BTaskStatus Status => _source?.Status ?? BTaskStatus.Succeeded;
    public bool IsCompleted => _source?.IsCompleted ?? true;
    public bool IsCompletedSuccessfully => _source?.IsCompletedSuccessfully ?? true;
    public bool IsCanceled => _source?.IsCanceled ?? false;
    public bool IsFaulted => _source?.IsFaulted ?? false;
    public Exception? Exception => _source?.Exception;

    public BValueTaskAwaiter GetAwaiter() => new(_source);
    public BTask AsBTask() => _source ?? BTask.CompletedTask;

    public static BValueTask FromException(Exception exception) =>
        new(BTask.FromException(exception));

    public static BValueTask FromCanceled(BCancellationToken cancellationToken) =>
        new(BTask.FromCanceled(cancellationToken));

    public static BValueTask<TResult> FromResult<TResult>(TResult result) => new(result);

    public static BValueTask<TResult> FromException<TResult>(Exception exception) =>
        new(BTask<TResult>.FromException(exception));

    public static BValueTask<TResult> FromCanceled<TResult>(BCancellationToken cancellationToken) =>
        new(BTask<TResult>.FromCanceled(cancellationToken));

    public static BValueTask Delay(TimeSpan delay, BCancellationToken cancellationToken = default) =>
        new(BTask.Delay(delay, cancellationToken));

    public static BValueTask Delay(int millisecondsDelay, BCancellationToken cancellationToken = default) =>
        new(BTask.Delay(millisecondsDelay, cancellationToken));

    public static BValueTask Yield() => new(BTask.Yield());

    public static BValueTask Run(Action action, BCancellationToken cancellationToken = default) =>
        new(BTask.Run(action, cancellationToken));

    public static BValueTask<TResult> Run<TResult>(
        Func<TResult> function,
        BCancellationToken cancellationToken = default) =>
        new(BTask.Run(function, cancellationToken));

    public static BValueTask Run(
        Func<BTask> function,
        BCancellationToken cancellationToken = default) =>
        new(BTask.Run(function, cancellationToken));

    public static BValueTask Run(
        Func<BValueTask> function,
        BCancellationToken cancellationToken = default) =>
        new(BTask.Run(function, cancellationToken));

    public static BValueTask WhenAll(params BTask[] tasks) => new(BTask.WhenAll(tasks));
    public static BValueTask WhenAll(IEnumerable<BTask> tasks) => new(BTask.WhenAll(tasks));

    public static BValueTask<BTask> WhenAny(params BTask[] tasks) =>
        new(BTask.WhenAny(tasks));

    public static BValueTask<BTask> WhenAny(IEnumerable<BTask> tasks) =>
        new(BTask.WhenAny(tasks));
}

public readonly struct BValueTaskAwaiter : IAwaiter, IBTaskAwaiter
{
    private readonly BTask? _source;

    internal BValueTaskAwaiter(BTask? source) => _source = source;

    public bool IsCompleted => _source?.IsCompleted ?? true;

    public void OnCompleted(Action continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        if (_source is null) BTaskScheduler.Main.Post(continuation);
        else _source.RegisterContinuation(continuation);
    }

    public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);
    public void GetResult() => _source?.GetResult();
}

public struct BValueTaskMethodBuilder
{
    private BValueTaskCompletionSource? _source;
    private BTaskMethodBuilderCore _core;
    private Exception? _failure;
    private BCancellationToken _cancellationToken;
    private BTaskStatus _status;

    public static BValueTaskMethodBuilder Create() => default;

    public BValueTask Task
    {
        get
        {
            if (_source is not null) return _source.Task;
            return _status switch
            {
                BTaskStatus.Succeeded => BValueTask.CompletedTask,
                BTaskStatus.Canceled => new BValueTask(BTask.CreateCanceled(_cancellationToken)),
                BTaskStatus.Faulted => BValueTask.FromException(_failure!),
                _ => EnsureSource().Task
            };
        }
    }

    public void SetResult()
    {
        if (_source is not null)
        {
            _source.SetResult();
            return;
        }
        EnsurePending();
        _status = BTaskStatus.Succeeded;
    }

    public void SetException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (_source is not null)
        {
            _source.SetException(exception);
            return;
        }
        EnsurePending();
        if (exception is BOperationCanceledException canceled)
        {
            _cancellationToken = canceled.CancellationToken;
            _status = BTaskStatus.Canceled;
        }
        else
        {
            _failure = exception;
            _status = BTaskStatus.Faulted;
        }
    }

    public void SetStateMachine(IAsyncStateMachine stateMachine) => _core.SetStateMachine(stateMachine);

    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine => _core.Start(ref stateMachine);

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        EnsureSource();
        _core.AwaitOnCompleted(ref awaiter, ref stateMachine);
    }

    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        EnsureSource();
        _core.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }

    private BValueTaskCompletionSource EnsureSource() =>
        _source ??= new BValueTaskCompletionSource();

    private readonly void EnsurePending()
    {
        if (_status != BTaskStatus.Pending)
            throw new InvalidOperationException("The BValueTask builder has already completed.");
    }
}
