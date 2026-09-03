using System.Runtime.CompilerServices;

namespace BEngine;

[AsyncMethodBuilder(typeof(BValueTaskMethodBuilder<>))]
public readonly struct BValueTask<TResult>
{
    private readonly ValueTask<TResult> _valueTask;

    public BValueTask(TResult result) => _valueTask = ValueTask.FromResult(result);

    public BValueTask(Task<TResult> task) =>
        _valueTask = new ValueTask<TResult>(task ?? throw new ArgumentNullException(nameof(task)));

    public BValueTask(ValueTask<TResult> valueTask) => _valueTask = valueTask;

    public BValueTask(BTask<TResult> task) : this(
        (task ?? throw new ArgumentNullException(nameof(task))).AsTask())
    {
    }

    public bool IsCompleted => _valueTask.IsCompleted;
    public bool IsCompletedSuccessfully => _valueTask.IsCompletedSuccessfully;
    public bool IsCanceled => _valueTask.IsCanceled;
    public bool IsFaulted => _valueTask.IsFaulted;

    public BValueTaskAwaiter<TResult> GetAwaiter() => new(_valueTask);

    public ConfiguredValueTaskAwaitable<TResult> ConfigureAwait(bool continueOnCapturedContext) =>
        _valueTask.ConfigureAwait(continueOnCapturedContext);

    public BValueTask<TResult> Preserve() => new(_valueTask.Preserve());

    public ValueTask<TResult> AsValueTask() => _valueTask;

    public Task<TResult> AsTask() => _valueTask.AsTask();

    public BValueTask<TResult> WaitAsync(CancellationToken cancellationToken) =>
        new(_valueTask.AsTask().WaitAsync(cancellationToken));

    public static BValueTask<TResult> FromResult(TResult result) => new(result);

    public static BValueTask<TResult> FromException(Exception exception) =>
        new(ValueTask.FromException<TResult>(
            exception ?? throw new ArgumentNullException(nameof(exception))));

    public static BValueTask<TResult> FromCanceled(CancellationToken cancellationToken) =>
        new(ValueTask.FromCanceled<TResult>(cancellationToken));

    public static implicit operator BValueTask<TResult>(ValueTask<TResult> task) => new(task);
    public static implicit operator BValueTask<TResult>(Task<TResult> task) => new(task);
    public static implicit operator BValueTask<TResult>(BTask<TResult> task) => new(task);
    public static implicit operator BValueTask<TResult>(TResult result) => new(result);
    public static explicit operator ValueTask<TResult>(BValueTask<TResult> task) => task._valueTask;
}

public readonly struct BValueTaskAwaiter<TResult> : IAwaiter<TResult>
{
    private readonly ValueTaskAwaiter<TResult> _awaiter;

    internal BValueTaskAwaiter(ValueTask<TResult> valueTask) => _awaiter = valueTask.GetAwaiter();

    public bool IsCompleted => _awaiter.IsCompleted;
    public void OnCompleted(Action continuation) => _awaiter.OnCompleted(continuation);
    public void UnsafeOnCompleted(Action continuation) => _awaiter.UnsafeOnCompleted(continuation);
    public TResult GetResult() => _awaiter.GetResult();
}

public struct BValueTaskMethodBuilder<TResult>
{
    private AsyncValueTaskMethodBuilder<TResult> _builder;

    public static BValueTaskMethodBuilder<TResult> Create() => new()
    {
        _builder = AsyncValueTaskMethodBuilder<TResult>.Create()
    };

    public BValueTask<TResult> Task => new(_builder.Task);

    public void SetResult(TResult result) => _builder.SetResult(result);
    public void SetException(Exception exception) => _builder.SetException(exception);
    public void SetStateMachine(IAsyncStateMachine stateMachine) => _builder.SetStateMachine(stateMachine);
    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine => _builder.Start(ref stateMachine);
    public void AwaitOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine =>
        _builder.AwaitOnCompleted(ref awaiter, ref stateMachine);
    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
        ref TAwaiter awaiter,
        ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine =>
        _builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
}
