namespace BEngine.Reactive;

public sealed class BehaviorSubject<T> : BObservable<T>, IDisposable
{
    private readonly Subject<T> _subject = new();

    public BehaviorSubject(T initialValue) => Value = initialValue;

    public T Value { get; private set; }

    public void OnNext(T value)
    {
        Value = value;
        _subject.OnNext(value);
    }

    public void OnErrorResume(Exception error) => _subject.OnErrorResume(error);
    public void OnCompleted() => _subject.OnCompleted();
    public void OnCompleted(Exception error) => _subject.OnCompleted(error);
    public void Dispose() => _subject.Dispose();

    protected override IDisposable SubscribeCore(BObserver<T> observer)
    {
        if (!_subject.IsCompleted) observer.OnNext(Value);
        return _subject.SubscribeDirect(observer);
    }
}
