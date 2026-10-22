namespace BEngine.Reactive;

internal sealed class AnonymousObserver<T>(
    Action<T>? onNext,
    Action<Exception>? onErrorResume,
    Action<BResult>? onCompleted) : BObserver<T>
{
    protected override void OnNextCore(T value) => onNext?.Invoke(value);

    protected override void OnErrorResumeCore(Exception error)
    {
        if (onErrorResume is null) base.OnErrorResumeCore(error);
        else onErrorResume(error);
    }

    protected override void OnCompletedCore(BResult result)
    {
        if (onCompleted is null) base.OnCompletedCore(result);
        else onCompleted(result);
    }
}
