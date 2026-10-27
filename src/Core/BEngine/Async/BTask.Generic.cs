using System.Runtime.CompilerServices;

namespace BEngine;

[AsyncMethodBuilder(typeof(BTaskMethodBuilder<>))]
public class BTask<TResult> : BTask
{
    private TResult? _result;

    protected internal BTask(BTaskScheduler scheduler, BTaskStatus status = BTaskStatus.Pending)
        : base(scheduler, status)
    {
    }

    public new BTaskAwaiter<TResult> GetAwaiter() => new(this);
    public TResult Result => GetResultValue();

    public static BTask<TResult> FromResult(TResult result)
    {
        var task = new BTask<TResult>(BTaskScheduler.Main);
        task.TrySetResult(result);
        return task;
    }

    public new static BTask<TResult> FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var task = new BTask<TResult>(BTaskScheduler.Main);
        task.TrySetException(exception);
        return task;
    }

    public new static BTask<TResult> FromCanceled(BCancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested)
            throw new ArgumentOutOfRangeException(nameof(cancellationToken),
                "The cancellation token must already be canceled.");
        return CreateCanceled(cancellationToken);
    }

    internal new static BTask<TResult> CreateCanceled(BCancellationToken cancellationToken)
    {
        var task = new BTask<TResult>(BTaskScheduler.Main);
        task.TrySetCanceled(cancellationToken);
        return task;
    }

    public static BTask<TResult[]> WhenAll(params BTask<TResult>[] tasks) =>
        BTaskCombinators.WhenAll((IEnumerable<BTask<TResult>>)tasks);

    public static BTask<TResult[]> WhenAll(IEnumerable<BTask<TResult>> tasks) =>
        BTaskCombinators.WhenAll(tasks);

    public static BTask<BTask<TResult>> WhenAny(params BTask<TResult>[] tasks) =>
        BTaskCombinators.WhenAny((IEnumerable<BTask<TResult>>)tasks);

    public static BTask<BTask<TResult>> WhenAny(IEnumerable<BTask<TResult>> tasks) =>
        BTaskCombinators.WhenAny(tasks);

    protected internal bool TrySetResult(TResult result)
    {
        if (Status != BTaskStatus.Pending) return false;
        _result = result;
        return base.TrySetResult();
    }

    internal TResult GetResultValue()
    {
        GetResult();
        return _result!;
    }
}

public readonly struct BTaskAwaiter<TResult> : IAwaiter<TResult>, IBTaskAwaiter
{
    private readonly BTask<TResult> _task;

    internal BTaskAwaiter(BTask<TResult> task) => _task = task;

    public bool IsCompleted => _task.IsCompleted;
    public void OnCompleted(Action continuation) => _task.RegisterContinuation(continuation);
    public void UnsafeOnCompleted(Action continuation) => _task.RegisterContinuation(continuation);
    public TResult GetResult() => _task.GetResultValue();
}

public struct BTaskMethodBuilder<TResult>
{
    private BTaskCompletionSource<TResult> _source;
    private BTaskMethodBuilderCore _core;

    public static BTaskMethodBuilder<TResult> Create() => new()
    {
        _source = new BTaskCompletionSource<TResult>()
    };

    public BTask<TResult> Task => _source.Task;
    public void SetResult(TResult result) => _source.SetResult(result);
    public void SetException(Exception exception) => _source.SetException(exception);
    public void SetStateMachine(IAsyncStateMachine stateMachine) => _core.SetStateMachine(stateMachine);
    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine => _core.Start(ref stateMachine);
    public void AwaitOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine =>
        _core.AwaitOnCompleted(ref awaiter, ref stateMachine);
    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine =>
        _core.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
}
