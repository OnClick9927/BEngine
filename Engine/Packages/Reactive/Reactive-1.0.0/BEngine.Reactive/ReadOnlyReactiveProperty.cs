namespace BEngine.Reactive;

public sealed class ReadOnlyReactiveProperty<T> : BObservable<T>, IDisposable
{
    private readonly ReactiveProperty<T>? _sourceProperty;
    private readonly BObservable<T> _source;
    private readonly IDisposable? _subscription;
    private readonly Subject<T>? _subject;
    private T _value;

    public ReadOnlyReactiveProperty(ReactiveProperty<T> source)
    {
        _sourceProperty = source ?? throw new ArgumentNullException(nameof(source));
        _source = source;
        _value = source.Value;
    }

    internal ReadOnlyReactiveProperty(BObservable<T> source, T initialValue)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _value = initialValue;
        _subject = new Subject<T>();
        _subscription = source.Subscribe(
            value =>
            {
                _value = value;
                _subject.OnNext(value);
            },
            _subject.OnErrorResume,
            result => _subject.OnCompleted(result));
    }

    public T Value => _sourceProperty is null ? _value : _sourceProperty.Value;
    public T CurrentValue => Value;

    public void Dispose()
    {
        _subscription?.Dispose();
        _subject?.Dispose();
    }

    protected override IDisposable SubscribeCore(BObserver<T> observer)
    {
        if (_sourceProperty is not null) return _source.SubscribeDirect(observer);
        observer.OnNext(_value);
        return _subject!.SubscribeDirect(observer);
    }
}
