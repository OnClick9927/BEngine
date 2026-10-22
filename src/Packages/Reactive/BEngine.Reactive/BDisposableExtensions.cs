namespace BEngine.Reactive;

public static class BDisposableExtensions
{
    public static T AddTo<T>(this T disposable, BCompositeDisposable destination) where T : IDisposable
    {
        destination.Add(disposable);
        return disposable;
    }

    public static T AddTo<T>(this T disposable, ref BDisposableBag destination) where T : IDisposable
    {
        destination.Add(disposable);
        return disposable;
    }
}
