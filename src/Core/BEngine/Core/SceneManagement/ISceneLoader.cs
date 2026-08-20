namespace BEngine.SceneManagement;

public interface ISceneLoader
{
    Scene LoadScene(string sceneNameOrPath, IServiceProvider services);
}
