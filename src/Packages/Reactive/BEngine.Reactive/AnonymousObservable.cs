namespace BEngine.Reactive;

internal sealed class AnonymousObservable<T>(
    Func<BObserver<T>, IDisposable?> subscribe) : BObservable<T>
{
    protected override IDisposable? SubscribeCore(BObserver<T> observer) => subscribe(observer);
}
