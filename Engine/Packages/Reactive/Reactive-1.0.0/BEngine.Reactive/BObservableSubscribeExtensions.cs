namespace BEngine.Reactive;

public static class BObservableSubscribeExtensions
{
    public static IDisposable Subscribe<T>(this BObservable<T> source) =>
        source.Subscribe(new AnonymousObserver<T>(null, null, null));

    public static IDisposable Subscribe<T>(this BObservable<T> source, Action<T> onNext) =>
        source.Subscribe(new AnonymousObserver<T>(
            onNext ?? throw new ArgumentNullException(nameof(onNext)), null, null));

    public static IDisposable Subscribe<T>(
        this BObservable<T> source,
        Action<T> onNext,
        Action<Exception> onErrorResume) =>
        source.Subscribe(new AnonymousObserver<T>(
            onNext ?? throw new ArgumentNullException(nameof(onNext)),
            onErrorResume ?? throw new ArgumentNullException(nameof(onErrorResume)), null));

    public static IDisposable Subscribe<T>(
        this BObservable<T> source,
        Action<T> onNext,
        Action<Exception> onErrorResume,
        Action<BResult> onCompleted) =>
        source.Subscribe(new AnonymousObserver<T>(
            onNext ?? throw new ArgumentNullException(nameof(onNext)),
            onErrorResume ?? throw new ArgumentNullException(nameof(onErrorResume)),
            onCompleted ?? throw new ArgumentNullException(nameof(onCompleted))));
}
