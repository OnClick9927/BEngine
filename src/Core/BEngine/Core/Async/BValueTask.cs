using System.Runtime.CompilerServices;

namespace BEngine;

/// <summary>
/// Allocation-conscious BEngine asynchronous operation. As with
/// <see cref="ValueTask"/>, an instance must only be awaited once unless it is
/// first converted with <see cref="Preserve"/> or <see cref="AsTask"/>.
/// </summary>
[AsyncMethodBuilder(typeof(BValueTaskMethodBuilder))]
public readonly struct BValueTask
{
    private readonly ValueTask _valueTask;

    public BValueTask(Task task) =>
        _valueTask = new ValueTask(task ?? throw new ArgumentNullException(nameof(task)));

    public BValueTask(ValueTask valueTask) => _valueTask = valueTask;

    public BValueTask(BTask task) : this(
        (task ?? throw new ArgumentNullException(nameof(task))).AsTask())
    {
    }

    public static BValueTask CompletedTask => default;

    public bool IsCompleted => _valueTask.IsCompleted;
    public bool IsCompletedSuccessfully => _valueTask.IsCompletedSuccessfully;
    public bool IsCanceled => _valueTask.IsCanceled;
    public bool IsFaulted => _valueTask.IsFaulted;

    public BValueTaskAwaiter GetAwaiter() => new(_valueTask);

    public ConfiguredValueTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) =>
        _valueTask.ConfigureAwait(continueOnCapturedContext);

    public BValueTask Preserve() => new(_valueTask.Preserve());

    public ValueTask AsValueTask() => _valueTask;

    public Task AsTask() => _valueTask.AsTask();

    public BValueTask WaitAsync(CancellationToken cancellationToken) =>
        new(_valueTask.AsTask().WaitAsync(cancellationToken));

    public static BValueTask FromException(Exception exception) =>
        new(ValueTask.FromException(
            exception ?? throw new ArgumentNullException(nameof(exception))));

    public static BValueTask FromCanceled(CancellationToken cancellationToken) =>
        new(ValueTask.FromCanceled(cancellationToken));

    public static implicit operator BValueTask(ValueTask task) => new(task);
    public static implicit operator BValueTask(Task task) => new(task);
    public static implicit operator BValueTask(BTask task) => new(task);
    public static explicit operator ValueTask(BValueTask task) => task._valueTask;
}

public readonly struct BValueTaskAwaiter : IAwaiter
{
    private readonly ValueTaskAwaiter _awaiter;

    internal BValueTaskAwaiter(ValueTask valueTask) => _awaiter = valueTask.GetAwaiter();

    public bool IsCompleted => _awaiter.IsCompleted;
    public void OnCompleted(Action continuation) => _awaiter.OnCompleted(continuation);
    public void UnsafeOnCompleted(Action continuation) => _awaiter.UnsafeOnCompleted(continuation);
    public void GetResult() => _awaiter.GetResult();
}

public struct BValueTaskMethodBuilder
{
    private AsyncValueTaskMethodBuilder _builder;

    public static BValueTaskMethodBuilder Create() => new()
    {
        _builder = AsyncValueTaskMethodBuilder.Create()
    };

    public BValueTask Task => new(_builder.Task);

    public void SetResult() => _builder.SetResult();
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
