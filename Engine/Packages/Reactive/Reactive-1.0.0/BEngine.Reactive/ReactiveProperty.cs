namespace BEngine.Reactive;

public class ReactiveProperty<T> : BObservable<T>, IDisposable
{
    private readonly Subject<T> _subject = new();
    private readonly IEqualityComparer<T> _comparer;
    private T _value;

    public ReactiveProperty() : this(default!) { }

    public ReactiveProperty(T initialValue) : this(initialValue, EqualityComparer<T>.Default) { }

    public ReactiveProperty(T initialValue, IEqualityComparer<T> comparer)
    {
        _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
        OnValueChanging(ref initialValue);
        _value = initialValue;
    }

    public T Value
    {
        get => _value;
        set
        {
            OnValueChanging(ref value);
            if (_comparer.Equals(_value, value)) return;
            _value = value;
            OnValueChanged(value);
            _subject.OnNext(value);
        }
    }

    public T CurrentValue => _value;
    public IEqualityComparer<T> EqualityComparer => _comparer;

    public bool IsDisposed => _subject.IsDisposed;

    public void OnNext(T value)
    {
        OnValueChanging(ref value);
        _value = value;
        OnValueChanged(value);
        _subject.OnNext(value);
    }

    public void ForceNotify() => OnNext(_value);

    public void OnErrorResume(Exception error) => _subject.OnErrorResume(error);
    public void OnCompleted() => _subject.OnCompleted();
    public void OnCompleted(Exception error) => _subject.OnCompleted(error);
    public void Dispose() => _subject.Dispose();

    public ReadOnlyReactiveProperty<T> ToReadOnlyReactiveProperty() => new(this);

    protected virtual void OnValueChanging(ref T value) { }
    protected virtual void OnValueChanged(T value) { }

    protected override IDisposable SubscribeCore(BObserver<T> observer)
    {
        if (!_subject.IsCompleted) observer.OnNext(_value);
        return _subject.SubscribeDirect(observer);
    }
}
