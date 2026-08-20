namespace BEngine.SceneManagement;

public sealed class RuntimeSceneManager(IServiceProvider services) : IRuntimeSceneManager
{
    private readonly List<Scene> _loadedScenes = [];
    private Scene? _activeScene;

    public event Action<Scene, LoadSceneMode>? SceneLoaded;
    public event Action<Scene>? SceneUnloaded;
    public event Action<Scene?, Scene?>? ActiveSceneChanged;

    public IReadOnlyList<Scene> LoadedScenes
    {
        get
        {
            MainThreadGuard.Ensure();
            return _loadedScenes;
        }
    }
    public Scene? ActiveScene
    {
        get
        {
            MainThreadGuard.Ensure();
            return _activeScene;
        }
    }
    public int SceneCount
    {
        get
        {
            MainThreadGuard.Ensure();
            return _loadedScenes.Count;
        }
    }

    public Scene LoadScene(string sceneNameOrPath, LoadSceneMode mode = LoadSceneMode.Single)
    {
        MainThreadGuard.Ensure();
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneNameOrPath);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var loader = services.GetService(typeof(ISceneLoader)) as ISceneLoader ??
            throw new InvalidOperationException(
                "No ISceneLoader is registered. Register the player or host scene loader with AddBEngine services.");
        var scene = loader.LoadScene(sceneNameOrPath, services) ??
            throw new InvalidOperationException($"The scene loader returned null for '{sceneNameOrPath}'.");
        if (_loadedScenes.Contains(scene))
            throw new InvalidOperationException("The scene loader returned a Scene that is already loaded.");
        if (string.IsNullOrWhiteSpace(scene.path)) scene.path = sceneNameOrPath;
        scene.isLoaded = true;

        var previousActive = _activeScene;
        if (mode == LoadSceneMode.Single)
        {
            var previousScenes = _loadedScenes.ToArray();
            foreach (var persistentRoot in previousScenes
                         .SelectMany(static item => item.rootGameObjects)
                         .Where(static item => item.isDontDestroyOnLoad)
                         .ToArray())
                Scene.MoveGameObjectToScene(persistentRoot, scene);

            _activeScene = null;
            foreach (var previous in previousScenes) UnloadSceneCore(previous);
            _loadedScenes.Clear();
        }

        RegisterSceneCore(scene);
        _activeScene = scene;
        SceneLoaded?.Invoke(scene, mode);
        if (!ReferenceEquals(previousActive, scene)) ActiveSceneChanged?.Invoke(previousActive, scene);
        return scene;
    }

    public void RegisterScene(Scene scene, bool setActive = false)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(scene);
        if (!scene.world.IsCreated)
            throw new ObjectDisposedException(nameof(scene), "An unloaded Scene cannot be registered.");
        RegisterSceneCore(scene);
        if (setActive || _activeScene is null) SetActiveScene(scene);
    }

    public bool UnloadScene(Scene scene)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(scene);
        if (!_loadedScenes.Contains(scene)) return false;
        if (_loadedScenes.Count == 1)
            throw new InvalidOperationException("The only loaded Scene cannot be unloaded.");

        return UnregisterScene(scene, disposeScene: true);
    }

    public bool UnregisterScene(Scene scene, bool disposeScene = false)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(scene);
        if (!_loadedScenes.Contains(scene)) return false;

        var previousActive = _activeScene;
        if (disposeScene && _loadedScenes.Count > 1)
        {
            var persistentDestination = ReferenceEquals(_activeScene, scene)
                ? _loadedScenes.Last(item => !ReferenceEquals(item, scene))
                : _activeScene ?? _loadedScenes.First(item => !ReferenceEquals(item, scene));
            foreach (var persistentRoot in scene.rootGameObjects
                         .Where(static item => item.isDontDestroyOnLoad)
                         .ToArray())
                Scene.MoveGameObjectToScene(persistentRoot, persistentDestination);
        }

        _loadedScenes.Remove(scene);
        if (disposeScene) UnloadSceneCore(scene);
        if (ReferenceEquals(_activeScene, scene))
            _activeScene = _loadedScenes.Count == 0 ? null : _loadedScenes[^1];
        if (!ReferenceEquals(previousActive, _activeScene))
            ActiveSceneChanged?.Invoke(previousActive, _activeScene);
        return true;
    }

    public bool SetActiveScene(Scene scene)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(scene);
        if (!_loadedScenes.Contains(scene) || !scene.isLoaded) return false;
        if (ReferenceEquals(_activeScene, scene)) return true;
        var previous = _activeScene;
        _activeScene = scene;
        ActiveSceneChanged?.Invoke(previous, scene);
        return true;
    }

    public void MoveGameObjectToScene(GameObject gameObject, Scene destination)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(gameObject);
        ArgumentNullException.ThrowIfNull(destination);
        if (!_loadedScenes.Contains(destination))
            throw new InvalidOperationException("The destination Scene is not registered as loaded.");
        if (gameObject.scene is not { } source || !_loadedScenes.Contains(source))
            throw new InvalidOperationException("The GameObject does not belong to a loaded Scene.");
        Scene.MoveGameObjectToScene(gameObject, destination);
        gameObject.ClearDontDestroyOnLoad();
    }

    public void MarkDontDestroyOnLoad(BObject target)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(target);
        var gameObject = target switch
        {
            GameObject value => value,
            Component value => value.gameObject,
            _ => throw new ArgumentException(
                "DontDestroyOnLoad only supports a GameObject or one of its Components.", nameof(target))
        };
        gameObject.MarkDontDestroyOnLoad();
    }

    private void UnloadSceneCore(Scene scene)
    {
        SceneRuntime.StopRunningScene(scene);
        foreach (var root in scene.rootGameObjects.ToArray()) scene.Destroy(root);
        scene.world.Dispose();
        scene.isLoaded = false;
        SceneUnloaded?.Invoke(scene);
    }

    private void RegisterSceneCore(Scene scene)
    {
        if (!_loadedScenes.Contains(scene)) _loadedScenes.Add(scene);
        scene.isLoaded = true;
    }
}
