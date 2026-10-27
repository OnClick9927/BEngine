namespace BEngine.SceneManagement;

public sealed class SceneUnloadOperation : AsyncOperation
{
    public bool succeeded { get; private set; }

    internal SceneUnloadOperation(Func<BCancellationToken, bool> unload)
    {
        ArgumentNullException.ThrowIfNull(unload);
        Start(async cancellationToken =>
        {
            using var loading = FrameWorkBudget.EnterLoading(cancellationToken);
            SetProgress(0.1f);
            succeeded = await BTask.Run(() => unload(cancellationToken), cancellationToken);
            SetProgress(0.99f);
        });
    }
}
