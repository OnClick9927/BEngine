namespace BEngine.SceneManagement;

public static class SceneManager
{
    public static event Action<Scene, LoadSceneMode>? sceneLoaded
    {
        add => Current.SceneLoaded += value;
        remove => Current.SceneLoaded -= value;
    }

    public static event Action<Scene>? sceneUnloaded
    {
        add => Current.SceneUnloaded += value;
        remove => Current.SceneUnloaded -= value;
    }

    public static event Action<Scene?, Scene?>? activeSceneChanged
    {
        add => Current.ActiveSceneChanged += value;
        remove => Current.ActiveSceneChanged -= value;
    }

    public static int sceneCount => Current.SceneCount;
    public static Scene? activeScene => Current.ActiveScene;

    public static Scene LoadScene(string sceneNameOrPath, LoadSceneMode mode = LoadSceneMode.Single) =>
        Current.LoadScene(sceneNameOrPath, mode);

    public static Scene? GetActiveScene() => Current.ActiveScene;
    public static Scene GetSceneAt(int index) => Current.LoadedScenes[index];
    public static Scene? GetSceneByName(string name) => Current.LoadedScenes.FirstOrDefault(
        item => string.Equals(item.name, name, StringComparison.Ordinal));
    public static Scene? GetSceneByPath(string path) => Current.LoadedScenes.FirstOrDefault(
        item => string.Equals(item.path, path, StringComparison.OrdinalIgnoreCase));
    public static bool SetActiveScene(Scene scene) => ManagerFor(scene).SetActiveScene(scene);
    public static bool UnregisterScene(Scene scene, bool disposeScene = false) =>
        ManagerFor(scene).UnregisterScene(scene, disposeScene);
    public static bool UnloadScene(Scene scene) => ManagerFor(scene).UnloadScene(scene);

    public static void MoveGameObjectToScene(GameObject gameObject, Scene destination) =>
        ManagerFor(gameObject.scene ?? destination).MoveGameObjectToScene(gameObject, destination);

    private static IRuntimeSceneManager Current
    {
        get
        {
            return SceneRuntime.currentScene?.Services.GetService(typeof(IRuntimeSceneManager)) as IRuntimeSceneManager ??
                   throw new InvalidOperationException(
                       "SceneManager requires an active SceneRuntime context. Hosts can resolve IRuntimeSceneManager from " +
                       "their BEngine service provider outside runtime callbacks.");
        }
    }

    private static IRuntimeSceneManager ManagerFor(Scene scene) =>
        scene.Services.GetService(typeof(IRuntimeSceneManager)) as IRuntimeSceneManager ?? Current;
}
