namespace BEngine.SceneManagement;

public sealed class SceneLoadOperation : AsyncOperation
{
    public Scene? preparedScene { get; private set; }
    public Scene? scene { get; private set; }

    internal SceneLoadOperation(
        Func<AsyncLoadContext, BTask<Scene>> loader,
        Func<Scene, Scene> activation)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(activation);
        Start(async cancellationToken =>
        {
            SetProgress(0.05f);
            var prepared = await loader(LoadContext);
            preparedScene = prepared ?? throw new InvalidOperationException("The scene loader returned null.");
            SetProgress(0.9f);
            try
            {
                await WaitForActivationAsync(cancellationToken);
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
