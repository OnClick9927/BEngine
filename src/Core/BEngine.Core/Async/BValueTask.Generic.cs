using System.Runtime.CompilerServices;

namespace BEngine;

[AsyncMethodBuilder(typeof(BValueTaskMethodBuilder<>))]
public readonly struct BValueTask<TResult>
{
    private readonly BTask<TResult>? _source;
    private readonly TResult? _result;

    public BValueTask(TResult result)
    {
        _source = null;
        _result = result;
    }

    public BValueTask(BTask<TResult> source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _result = default;
    }

    public BTaskStatus Status => _source?.Status ?? BTaskStatus.Succeeded;
    public bool IsCompleted => _source?.IsCompleted ?? true;
    public bool IsCompletedSuccessfully => _source?.IsCompletedSuccessfully ?? true;
    public bool IsCanceled => _source?.IsCanceled ?? false;
    public bool IsFaulted => _source?.IsFaulted ?? false;
    public Exception? Exception => _source?.Exception;
    public TResult Result => GetAwaiter().GetResult();

    public BValueTaskAwaiter<TResult> GetAwaiter() => new(_source, _result);
    public BTask<TResult> AsBTask() => _source ?? BTask<TResult>.FromResult(_result!);

    public static BValueTask<TResult> FromResult(TResult result) => new(result);

    public static BValueTask<TResult> FromException(Exception exception) =>
        new(BTask<TResult>.FromException(exception));

    public static BValueTask<TResult> FromCanceled(BCancellationToken cancellationToken) =>
        new(BTask<TResult>.FromCanceled(cancellationToken));

    public static BValueTask<TResult[]> WhenAll(params BTask<TResult>[] tasks) =>
        new(BTask<TResult>.WhenAll(tasks));

    public static BValueTask<TResult[]> WhenAll(IEnumerable<BTask<TResult>> tasks) =>
        new(BTask<TResult>.WhenAll(tasks));

    public static BValueTask<BTask<TResult>> WhenAny(params BTask<TResult>[] tasks) =>
        new(BTask<TResult>.WhenAny(tasks));

    public static BValueTask<BTask<TResult>> WhenAny(IEnumerable<BTask<TResult>> tasks) =>
        new(BTask<TResult>.WhenAny(tasks));
}

public readonly struct BValueTaskAwaiter<TResult> : IAwaiter<TResult>, IBTaskAwaiter
{
    private readonly BTask<TResult>? _source;
    private readonly TResult? _result;

    internal BValueTaskAwaiter(BTask<TResult>? source, TResult? result)
    {
        _source = source;
        _result = result;
    }

    public bool IsCompleted => _source?.IsCompleted ?? true;

    public void OnCompleted(Action continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        if (_source is null) BTaskScheduler.Main.Post(continuation);
        else _source.RegisterContinuation(continuation);
    }

    public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);
    public TResult GetResult() => _source is null ? _result! : _source.GetResultValue();
}

public struct BValueTaskMethodBuilder<TResult>
{
    private BValueTaskCompletionSource<TResult>? _source;
    private BTaskMethodBuilderCore _core;
    private Exception? _failure;
    private BCancellationToken _cancellationToken;
    private TResult? _result;
    private BTaskStatus _status;

    public static BValueTaskMethodBuilder<TResult> Create() => default;

    public BValueTask<TResult> Task
    {
        get
        {
            if (_source is not null) return _source.Task;
            return _status switch
            {
                BTaskStatus.Succeeded => new BValueTask<TResult>(_result!),
                BTaskStatus.Canceled => new BValueTask<TResult>(BTask<TResult>.CreateCanceled(_cancellationToken)),
                BTaskStatus.Faulted => BValueTask<TResult>.FromException(_failure!),
                _ => EnsureSource().Task
            };
        }
    }

    public void SetResult(TResult result)
    {
        if (_source is not null)
        {
            _source.SetResult(result);
            return;
        }
        EnsurePending();
        _result = result;
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

    private BValueTaskCompletionSource<TResult> EnsureSource() =>
        _source ??= new BValueTaskCompletionSource<TResult>();

    private readonly void EnsurePending()
    {
        if (_status != BTaskStatus.Pending)
            throw new InvalidOperationException("The BValueTask builder has already completed.");
    }
}
