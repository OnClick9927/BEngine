namespace BEngine.Reactive;

public abstract class BObservable<T>
{
    public IDisposable Subscribe(BObserver<T> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var trackingId = BObservableTracker.Add(GetType());
        observer.SetTrackingId(trackingId);
        IDisposable upstream;
        try { upstream = SubscribeCore(observer) ?? BDisposable.Empty; }
        catch (Exception exception)
        {
            observer.OnCompleted(exception);
            return observer;
        }
        observer.SetUpstream(upstream);
        return observer;
    }

    internal IDisposable SubscribeDirect(BObserver<T> observer) =>
        SubscribeCore(observer) ?? BDisposable.Empty;

    protected abstract IDisposable? SubscribeCore(BObserver<T> observer);
}
