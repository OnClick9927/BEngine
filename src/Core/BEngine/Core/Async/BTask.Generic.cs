using System.Runtime.CompilerServices;

namespace BEngine;

[AsyncMethodBuilder(typeof(BTaskMethodBuilder<>))]
public sealed class BTask<TResult> : BTask
{
    private readonly Task<TResult> _task;

    private BTask(Task<TResult> task) : base(task) => _task = task;

    public new BTaskAwaiter<TResult> GetAwaiter() => new(_task);

    public new ConfiguredTaskAwaitable<TResult> ConfigureAwait(bool continueOnCapturedContext) =>
        _task.ConfigureAwait(continueOnCapturedContext);

    public new Task<TResult> AsTask() => _task;

    public new BValueTask<TResult> AsValueTask() => new(_task);

    public new BTask<TResult> WaitAsync(CancellationToken cancellationToken) =>
        FromTask(_task.WaitAsync(cancellationToken));

    public new BTask<TResult> WaitAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        FromTask(_task.WaitAsync(timeout, cancellationToken));

    public static BTask<TResult> FromTask(Task<TResult> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new BTask<TResult>(task);
    }

    public static BTask<TResult> FromResult(TResult result) =>
        FromTask(Task.FromResult(result));

    public new static BTask<TResult> FromException(Exception exception) =>
        FromTask(Task.FromException<TResult>(
            exception ?? throw new ArgumentNullException(nameof(exception))));

    public new static BTask<TResult> FromCanceled(CancellationToken cancellationToken) =>
        FromTask(Task.FromCanceled<TResult>(cancellationToken));

    public static BTask<TResult> Run(
        Func<TResult> function,
        CancellationToken cancellationToken = default) =>
        FromTask(Task.Run(
            function ?? throw new ArgumentNullException(nameof(function)), cancellationToken));

    public static BTask<TResult> Run(
        Func<Task<TResult>> function,
        CancellationToken cancellationToken = default) =>
        FromTask(Task.Run(
            function ?? throw new ArgumentNullException(nameof(function)), cancellationToken));

    public static BTask<TResult> Run(
        Func<BTask<TResult>> function,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);
        return FromTask(Task.Run(async () =>
            await function().ConfigureAwait(false), cancellationToken));
    }

    public static BTask<TResult[]> WhenAll(params BTask<TResult>[] tasks) =>
        WhenAll((IEnumerable<BTask<TResult>>)tasks);

    public static BTask<TResult[]> WhenAll(IEnumerable<BTask<TResult>> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return BTask<TResult[]>.FromTask(Task.WhenAll(tasks.Select(static task =>
            (task ?? throw new ArgumentException("The task collection contains a null task."))._task)));
    }

    public static BTask<BTask<TResult>> WhenAny(params BTask<TResult>[] tasks) =>
        WhenAny((IEnumerable<BTask<TResult>>)tasks);

    public static BTask<BTask<TResult>> WhenAny(IEnumerable<BTask<TResult>> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var materialized = tasks.ToArray();
        if (materialized.Any(static task => task is null))
            throw new ArgumentException("The task collection contains a null task.", nameof(tasks));
        return WhenAnyCore(materialized);
    }

    public static implicit operator BTask<TResult>(Task<TResult> task) => FromTask(task);

    public static implicit operator Task<TResult>(BTask<TResult> task) =>
        (task ?? throw new ArgumentNullException(nameof(task)))._task;

    private static async BTask<BTask<TResult>> WhenAnyCore(BTask<TResult>[] tasks)
    {
        var completed = await Task.WhenAny(tasks.Select(static task => task._task)).ConfigureAwait(false);
        return tasks.First(task => ReferenceEquals(task._task, completed));
    }
}

public readonly struct BTaskAwaiter<TResult> : IAwaiter<TResult>
{
    private readonly TaskAwaiter<TResult> _awaiter;

    internal BTaskAwaiter(Task<TResult> task) => _awaiter = task.GetAwaiter();

    public bool IsCompleted => _awaiter.IsCompleted;
    public void OnCompleted(Action continuation) => _awaiter.OnCompleted(continuation);
    public void UnsafeOnCompleted(Action continuation) => _awaiter.UnsafeOnCompleted(continuation);
    public TResult GetResult() => _awaiter.GetResult();
}

public struct BTaskMethodBuilder<TResult>
{
    private AsyncTaskMethodBuilder<TResult> _builder;
    private BTask<TResult>? _task;

    public static BTaskMethodBuilder<TResult> Create() => new()
    {
        _builder = AsyncTaskMethodBuilder<TResult>.Create()
    };

    public BTask<TResult> Task => _task ??= BTask<TResult>.FromTask(_builder.Task);

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
