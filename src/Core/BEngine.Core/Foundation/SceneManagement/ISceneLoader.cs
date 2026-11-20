namespace BEngine.SceneManagement;

public interface ISceneLoader
{
    Scene LoadScene(string sceneNameOrPath, IServiceProvider services);
}

/// <summary>Optional resumable main-thread scene loading. Legacy loaders retain their synchronous fallback.</summary>
public interface IAsyncSceneLoader : ISceneLoader
{
    BTask<Scene> LoadSceneAsync(string sceneNameOrPath, IServiceProvider services, AsyncLoadContext context);
}
