using System.Runtime.CompilerServices;

namespace BEngine;

/// <summary>
/// BEngine's reference-type asynchronous operation. It preserves the runtime's
/// standard exception, cancellation and scheduling semantics while keeping
/// engine-facing APIs independent from <see cref="System.Threading.Tasks.Task"/>.
/// </summary>
[AsyncMethodBuilder(typeof(BTaskMethodBuilder))]
public class BTask
{
    private readonly Task _task;

    private protected BTask(Task task) =>
        _task = task ?? throw new ArgumentNullException(nameof(task));

    public static BTask CompletedTask { get; } = new(Task.CompletedTask);

    public bool IsCompleted => _task.IsCompleted;
    public bool IsCompletedSuccessfully => _task.IsCompletedSuccessfully;
    public bool IsCanceled => _task.IsCanceled;
    public bool IsFaulted => _task.IsFaulted;
    public AggregateException? Exception => _task.Exception;
    public TaskStatus Status => _task.Status;

    public BTaskAwaiter GetAwaiter() => new(_task);

    public ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) =>
        _task.ConfigureAwait(continueOnCapturedContext);

    public Task AsTask() => _task;

    public BValueTask AsValueTask() => new(_task);

    public BTask WaitAsync(CancellationToken cancellationToken) =>
        FromTask(_task.WaitAsync(cancellationToken));

    public BTask WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        FromTask(_task.WaitAsync(timeout, cancellationToken));

    public static BTask FromTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task.IsCompletedSuccessfully ? CompletedTask : new BTask(task);
    }

    public static BTask FromException(Exception exception) =>
        FromTask(Task.FromException(exception ?? throw new ArgumentNullException(nameof(exception))));

    public static BTask FromCanceled(CancellationToken cancellationToken) =>
        FromTask(Task.FromCanceled(cancellationToken));

    public static BTask Delay(TimeSpan delay, CancellationToken cancellationToken = default) =>
        FromTask(Task.Delay(delay, cancellationToken));

    public static BTask Delay(int millisecondsDelay, CancellationToken cancellationToken = default) =>
        FromTask(Task.Delay(millisecondsDelay, cancellationToken));

    public static BTask Yield() => YieldCore();

    public static BTask Run(Action action, CancellationToken cancellationToken = default) =>
        FromTask(Task.Run(action ?? throw new ArgumentNullException(nameof(action)), cancellationToken));

    public static BTask Run(Func<Task> action, CancellationToken cancellationToken = default) =>
        FromTask(Task.Run(action ?? throw new ArgumentNullException(nameof(action)), cancellationToken));

    public static BTask Run(Func<BTask> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return FromTask(Task.Run(async () =>
            await action().ConfigureAwait(false), cancellationToken));
    }

    public static BTask Run(Func<BValueTask> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return FromTask(Task.Run(async () =>
            await action().ConfigureAwait(false), cancellationToken));
    }

    public static BTask WhenAll(params BTask[] tasks) =>
        WhenAll((IEnumerable<BTask>)tasks);

    public static BTask WhenAll(IEnumerable<BTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return FromTask(Task.WhenAll(tasks.Select(GetInnerTask)));
    }

    public static BTask<BTask> WhenAny(params BTask[] tasks) =>
        WhenAny((IEnumerable<BTask>)tasks);

    public static BTask<BTask> WhenAny(IEnumerable<BTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var materialized = tasks.ToArray();
        if (materialized.Any(static task => task is null))
            throw new ArgumentException("The task collection contains a null task.", nameof(tasks));
        return WhenAnyCore(materialized);
    }

    public static implicit operator BTask(Task task) => FromTask(task);

    public static implicit operator Task(BTask task) =>
        (task ?? throw new ArgumentNullException(nameof(task)))._task;

    private static Task GetInnerTask(BTask task) =>
        (task ?? throw new ArgumentException("The task collection contains a null task."))._task;

    private static async BTask YieldCore() => await Task.Yield();

    private static async BTask<BTask> WhenAnyCore(BTask[] tasks)
    {
        var completed = await Task.WhenAny(tasks.Select(static task => task._task)).ConfigureAwait(false);
        return tasks.First(task => ReferenceEquals(task._task, completed));
    }
}

public readonly struct BTaskAwaiter : IAwaiter
{
    private readonly TaskAwaiter _awaiter;

    internal BTaskAwaiter(Task task) => _awaiter = task.GetAwaiter();

    public bool IsCompleted => _awaiter.IsCompleted;
    public void OnCompleted(Action continuation) => _awaiter.OnCompleted(continuation);
    public void UnsafeOnCompleted(Action continuation) => _awaiter.UnsafeOnCompleted(continuation);
    public void GetResult() => _awaiter.GetResult();
}

public struct BTaskMethodBuilder
{
    private AsyncTaskMethodBuilder _builder;
    private BTask? _task;

    public static BTaskMethodBuilder Create() => new()
    {
        _builder = AsyncTaskMethodBuilder.Create()
    };

    public BTask Task => _task ??= BTask.FromTask(_builder.Task);

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
