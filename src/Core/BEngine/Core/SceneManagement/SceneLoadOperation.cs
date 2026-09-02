namespace BEngine.SceneManagement;

public sealed class SceneLoadOperation : AsyncOperation
{
    public Scene? preparedScene { get; private set; }
    public Scene? scene { get; private set; }

    internal SceneLoadOperation(
        Func<CancellationToken, Scene> loader,
        Func<Scene, Scene> activation)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(activation);
        Start(async cancellationToken =>
        {
            SetProgress(0.05f);
            var prepared = await Task.Run(() => loader(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            preparedScene = prepared ?? throw new InvalidOperationException("The scene loader returned null.");
            SetProgress(0.9f);
            try
            {
                await WaitForActivationAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                scene = activation(preparedScene);
                SetProgress(0.99f);
            }
            finally
            {
                if (scene is null && prepared.isCreated) prepared.Dispose();
            }
        });
    }
}
