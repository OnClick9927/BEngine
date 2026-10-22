using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Reflection;

namespace BEngine;

[AsyncMethodBuilder(typeof(BTaskMethodBuilder))]
public class BTask
{
    private readonly BTaskScheduler _scheduler;
    private Action? _firstContinuation;
    private List<Action>? _additionalContinuations;
    private ExceptionDispatchInfo? _failure;
    private BCancellationToken _cancellationToken;
    private BTaskStatus _status;

    protected internal BTask(BTaskScheduler scheduler, BTaskStatus status = BTaskStatus.Pending)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _status = status;
    }

    public static BTask CompletedTask { get; } =
        new(BTaskScheduler.Main, BTaskStatus.Succeeded);

    public BTaskStatus Status => _status;
    public bool IsCompleted => _status != BTaskStatus.Pending;
    public bool IsCompletedSuccessfully => _status == BTaskStatus.Succeeded;
    public bool IsCanceled => _status == BTaskStatus.Canceled;
    public bool IsFaulted => _status == BTaskStatus.Faulted;
    public Exception? Exception => _failure?.SourceException;

    public BTaskAwaiter GetAwaiter() => new(this);

    public static BTask FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var task = new BTask(BTaskScheduler.Main);
        task.TrySetException(exception);
        return task;
    }

    public static BTask FromCanceled(BCancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested)
            throw new ArgumentOutOfRangeException(nameof(cancellationToken),
                "The cancellation token must already be canceled.");
        return CreateCanceled(cancellationToken);
    }

    internal static BTask CreateCanceled(BCancellationToken cancellationToken)
    {
        var task = new BTask(BTaskScheduler.Main);
        task.TrySetCanceled(cancellationToken);
        return task;
    }

    public static BTask<TResult> FromResult<TResult>(TResult result) =>
        BTask<TResult>.FromResult(result);

    public static BTask<TResult> FromException<TResult>(Exception exception) =>
        BTask<TResult>.FromException(exception);

    public static BTask<TResult> FromCanceled<TResult>(BCancellationToken cancellationToken) =>
        BTask<TResult>.FromCanceled(cancellationToken);

    public static BTask Delay(TimeSpan delay, BCancellationToken cancellationToken = default) =>
        BTaskScheduler.Main.ScheduleDelay(delay, cancellationToken);

    public static BTask Delay(int millisecondsDelay, BCancellationToken cancellationToken = default) =>
        Delay(TimeSpan.FromMilliseconds(millisecondsDelay), cancellationToken);

    public static BTask Yield() => Delay(TimeSpan.Zero);

    public static BTask Run(Action action, BCancellationToken cancellationToken = default) =>
        BTaskScheduler.Main.Schedule(action, cancellationToken);

    public static BTask<TResult> Run<TResult>(
        Func<TResult> function,
        BCancellationToken cancellationToken = default) =>
        BTaskScheduler.Main.Schedule(function, cancellationToken);

    public static BTask Run(Func<BTask> function, BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);
        return RunDeferred(function, cancellationToken);
    }

    public static BTask Run(Func<BValueTask> function, BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(function);
        return RunDeferred(function, cancellationToken);
    }

    public static BTask WhenAll(params BTask[] tasks) => WhenAll((IEnumerable<BTask>)tasks);

    public static BTask WhenAll(IEnumerable<BTask> tasks)
    {
        var values = Materialize(tasks);
        if (values.Length == 0) return CompletedTask;
        var source = new BTaskCompletionSource();
        var remaining = values.Length;
        List<Exception>? failures = null;
        var canceled = false;
        var cancellationToken = default(BCancellationToken);
        foreach (var task in values)
        {
            if (task.IsCompleted) Complete(task);
            else task.RegisterContinuation(() => Complete(task));
        }
        return source.Task;

        void Complete(BTask task)
        {
            try { task.GetResult(); }
            catch (BOperationCanceledException exception)
            {
                canceled = true;
                cancellationToken = exception.CancellationToken;
            }
            catch (Exception exception) { (failures ??= []).Add(exception); }
            remaining--;
            if (remaining != 0) return;
            if (failures is { Count: > 0 })
                source.TrySetException(failures.Count == 1
                    ? failures[0]
                    : new AggregateException(failures));
            else if (canceled)
                source.TrySetCanceled(cancellationToken);
            else
                source.TrySetResult();
        }
    }

    public static BTask<BTask> WhenAny(params BTask[] tasks) =>
        WhenAny((IEnumerable<BTask>)tasks);

    public static BTask<BTask> WhenAny(IEnumerable<BTask> tasks)
    {
        var values = Materialize(tasks);
        if (values.Length == 0)
            throw new ArgumentException("At least one task is required.", nameof(tasks));
        var completed = values.FirstOrDefault(static task => task.IsCompleted);
        if (completed is not null) return BTask<BTask>.FromResult(completed);
        var source = new BTaskCompletionSource<BTask>();
        var won = false;
        foreach (var task in values)
            task.RegisterContinuation(() =>
            {
                if (won) return;
                won = true;
                source.TrySetResult(task);
            });
        return source.Task;
    }

    public static BTask<TResult[]> WhenAll<TResult>(params BTask<TResult>[] tasks) =>
        BTask<TResult>.WhenAll(tasks);

    public static BTask<TResult[]> WhenAll<TResult>(IEnumerable<BTask<TResult>> tasks) =>
        BTask<TResult>.WhenAll(tasks);

    public static BTask<BTask<TResult>> WhenAny<TResult>(params BTask<TResult>[] tasks) =>
        BTask<TResult>.WhenAny(tasks);

    public static BTask<BTask<TResult>> WhenAny<TResult>(IEnumerable<BTask<TResult>> tasks) =>
        BTask<TResult>.WhenAny(tasks);

    internal BTaskScheduler Scheduler => _scheduler;

    internal void RegisterContinuation(Action continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        if (IsCompleted)
        {
            _scheduler.Post(continuation);
            return;
        }
        if (_firstContinuation is null) _firstContinuation = continuation;
        else (_additionalContinuations ??= []).Add(continuation);
    }

    internal bool RemoveContinuationsReferencing(IReadOnlySet<Assembly> assemblies)
    {
        var removed = false;
        if (_firstContinuation is not null &&
            BTaskScheduler.CallbackReferencesAnyAssembly(_firstContinuation, assemblies))
        {
            _firstContinuation = null;
            removed = true;
        }
        if (_additionalContinuations is not null)
        {
            removed |= _additionalContinuations.RemoveAll(continuation =>
                BTaskScheduler.CallbackReferencesAnyAssembly(continuation, assemblies)) > 0;
            if (_additionalContinuations.Count == 0) _additionalContinuations = null;
        }
        return removed;
    }

    internal bool HasContinuations =>
        _firstContinuation is not null || _additionalContinuations is { Count: > 0 };

    protected internal bool TrySetResult()
    {
        return TryComplete(BTaskStatus.Succeeded, null, default);
    }

    protected internal bool TrySetException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is BOperationCanceledException canceled
            ? TryComplete(BTaskStatus.Canceled, null, canceled.CancellationToken)
            : TryComplete(BTaskStatus.Faulted, ExceptionDispatchInfo.Capture(exception), default);
    }

    protected internal bool TrySetCanceled(BCancellationToken cancellationToken)
    {
        return TryComplete(BTaskStatus.Canceled, null, cancellationToken);
    }

    internal void GetResult()
    {
        switch (_status)
        {
            case BTaskStatus.Succeeded:
                return;
            case BTaskStatus.Canceled:
                throw new BOperationCanceledException(_cancellationToken);
            case BTaskStatus.Faulted:
                _failure!.Throw();
                return;
            default:
                throw new InvalidOperationException("The BTask has not completed.");
        }
    }

    private bool TryComplete(
        BTaskStatus status,
        ExceptionDispatchInfo? failure,
        BCancellationToken cancellationToken)
    {
        if (_status != BTaskStatus.Pending) return false;
        _status = status;
        _failure = failure;
        _cancellationToken = cancellationToken;
        ScheduleContinuations();
        return true;
    }

    private void ScheduleContinuations()
    {
        if (_firstContinuation is not null) _scheduler.Post(_firstContinuation);
        if (_additionalContinuations is not null)
            foreach (var continuation in _additionalContinuations) _scheduler.Post(continuation);
        _firstContinuation = null;
        _additionalContinuations = null;
    }

    private static BTask[] Materialize(IEnumerable<BTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var values = tasks.ToArray();
        if (values.Any(static task => task is null))
            throw new ArgumentException("The task collection contains a null task.", nameof(tasks));
        return values;
    }

    private static async BTask RunDeferred(
        Func<BTask> function,
        BCancellationToken cancellationToken)
    {
        await Yield();
        cancellationToken.ThrowIfCancellationRequested();
        await function();
    }

    private static async BTask RunDeferred(
        Func<BValueTask> function,
        BCancellationToken cancellationToken)
    {
        await Yield();
        cancellationToken.ThrowIfCancellationRequested();
        await function();
    }
}

public readonly struct BTaskAwaiter : IAwaiter, IBTaskAwaiter
{
    private readonly BTask _task;

    internal BTaskAwaiter(BTask task) => _task = task;

    public bool IsCompleted => _task.IsCompleted;
    public void OnCompleted(Action continuation) => _task.RegisterContinuation(continuation);
    public void UnsafeOnCompleted(Action continuation) => _task.RegisterContinuation(continuation);
    public void GetResult() => _task.GetResult();
}

public struct BTaskMethodBuilder
{
    private BTaskCompletionSource _source;
    private BTaskMethodBuilderCore _core;

    public static BTaskMethodBuilder Create() => new()
    {
        _source = new BTaskCompletionSource()
    };

    public BTask Task => _source.Task;
    public void SetResult() => _source.SetResult();
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
