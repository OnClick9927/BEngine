namespace BEngine.SceneManagement;

public interface IRuntimeSceneManager
{
    event Action<Scene, LoadSceneMode>? SceneLoaded;
    event Action<Scene>? SceneUnloaded;
    event Action<Scene?, Scene?>? ActiveSceneChanged;

    IReadOnlyList<Scene> LoadedScenes { get; }
    Scene? ActiveScene { get; }
    int SceneCount { get; }

    Scene LoadScene(string sceneNameOrPath, LoadSceneMode mode = LoadSceneMode.Single);
    void RegisterScene(Scene scene, bool setActive = false);
    bool UnregisterScene(Scene scene, bool disposeScene = false);
    bool UnloadScene(Scene scene);
    bool SetActiveScene(Scene scene);
    void MoveGameObjectToScene(GameObject gameObject, Scene destination);
    void MarkDontDestroyOnLoad(BObject target);
}
