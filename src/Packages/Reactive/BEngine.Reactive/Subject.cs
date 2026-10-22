using System.Buffers;

namespace BEngine.Reactive;

public class Subject<T> : BObservable<T>, IDisposable
{
    private Subscription? _first;
    private Subscription? _last;
    private BResult _completion;
    private bool _completed;
    private int _subscriberCount;

    public bool IsCompleted => _completed;
    public bool IsDisposed => _completed;

    public void OnNext(T value)
    {
        if (_completed) return;
        var snapshot = CaptureSubscribers(out var count);
        try
        {
            for (var index = 0; index < count && !_completed; index++)
                snapshot[index].Observer.OnNext(value);
        }
        finally { ArrayPool<Subscription>.Shared.Return(snapshot, clearArray: true); }
    }

    public void OnErrorResume(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (_completed) return;
        var snapshot = CaptureSubscribers(out var count);
        try
        {
            for (var index = 0; index < count && !_completed; index++)
                snapshot[index].Observer.OnErrorResume(error);
        }
        finally { ArrayPool<Subscription>.Shared.Return(snapshot, clearArray: true); }
    }

    public void OnCompleted() => OnCompleted(BResult.Success);
    public void OnCompleted(Exception error) => OnCompleted(BResult.Failure(error));

    public void OnCompleted(BResult result)
    {
        if (_completed) return;
        _completed = true;
        _completion = result;
        while (_first is { } current)
        {
            Remove(current);
            current.Observer.OnCompleted(result);
        }
    }

    public void Dispose() => OnCompleted();

    protected override IDisposable SubscribeCore(BObserver<T> observer)
    {
        if (_completed)
        {
            observer.OnCompleted(_completion);
            return BDisposable.Empty;
        }
        var subscription = new Subscription(this, observer);
        if (_last is null) _first = subscription;
        else
        {
            _last.Next = subscription;
            subscription.Previous = _last;
        }
        _last = subscription;
        _subscriberCount++;
        return subscription;
    }

    private Subscription[] CaptureSubscribers(out int count)
    {
        var snapshot = ArrayPool<Subscription>.Shared.Rent(_subscriberCount);
        count = 0;
        for (var current = _first; current is not null; current = current.Next)
            snapshot[count++] = current;
        return snapshot;
    }

    private void Remove(Subscription subscription)
    {
        if (subscription.Previous is { } previous) previous.Next = subscription.Next;
        else if (ReferenceEquals(_first, subscription)) _first = subscription.Next;
        if (subscription.Next is { } next) next.Previous = subscription.Previous;
        else if (ReferenceEquals(_last, subscription)) _last = subscription.Previous;
        _subscriberCount--;
        subscription.DetachWithoutOwner();
    }

    private sealed class Subscription(Subject<T> owner, BObserver<T> observer) : IDisposable
    {
        private Subject<T>? _owner = owner;
        internal BObserver<T> Observer { get; } = observer;
        internal Subscription? Previous { get; set; }
        internal Subscription? Next { get; set; }

        public void Dispose()
        {
            var current = _owner;
            if (current is not null) current.Remove(this);
        }

        internal void DetachWithoutOwner()
        {
            _owner = null;
            Previous = null;
            Next = null;
        }
    }
}
