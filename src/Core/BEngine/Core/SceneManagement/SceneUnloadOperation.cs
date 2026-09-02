namespace BEngine.SceneManagement;

public sealed class SceneUnloadOperation : AsyncOperation
{
    public bool succeeded { get; private set; }

    internal SceneUnloadOperation(Func<CancellationToken, bool> unload)
    {
        ArgumentNullException.ThrowIfNull(unload);
        Start(async cancellationToken =>
        {
            SetProgress(0.1f);
            succeeded = await Task.Run(() => unload(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            SetProgress(0.99f);
        });
    }
}
