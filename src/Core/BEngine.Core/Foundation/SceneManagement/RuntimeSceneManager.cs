namespace BEngine.SceneManagement;

public sealed class RuntimeSceneManager(IServiceProvider services) : IRuntimeSceneManager, IDisposable
{
    private readonly List<Scene> _loadedScenes = [];
    private readonly HashSet<AsyncOperation> _pendingOperations = [];
    private Scene? _activeScene;
    private bool _disposed;

    public event Action<Scene, LoadSceneMode>? SceneLoaded;
    public event Action<Scene>? SceneUnloaded;
    public event Action<Scene?, Scene?>? ActiveSceneChanged;

    public IReadOnlyList<Scene> LoadedScenes
    {
        get
        {
            return _loadedScenes;
        }
    }
    public Scene? ActiveScene
    {
        get
        {
            return _activeScene;
        }
    }
    public int SceneCount
    {
        get
        {
            return _loadedScenes.Count;
        }
    }

    public Scene LoadScene(string sceneNameOrPath, LoadSceneMode mode = LoadSceneMode.Single)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneNameOrPath);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var scene = PrepareScene(sceneNameOrPath, BCancellationToken.None);
        try { return ActivateScene(scene, sceneNameOrPath, mode); }
        catch
        {
            if (!_loadedScenes.Contains(scene) && scene.isCreated) scene.Dispose();
            throw;
        }
    }

    public SceneLoadOperation LoadSceneAsync(
        string sceneNameOrPath,
        LoadSceneMode mode = LoadSceneMode.Single)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneNameOrPath);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        return Track(new SceneLoadOperation(
            context => PrepareSceneAsync(sceneNameOrPath, context),
            scene => ActivateScene(scene, sceneNameOrPath, mode)));
    }

    private Scene PrepareScene(string sceneNameOrPath, BCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var loader = services.GetService(typeof(ISceneLoader)) as ISceneLoader ??
                     throw new InvalidOperationException(
                         "No ISceneLoader is registered. Register the player or host scene loader with AddBEngine services.");
        var scene = loader.LoadScene(sceneNameOrPath, services) ??
                    throw new InvalidOperationException($"The scene loader returned null for '{sceneNameOrPath}'.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            return scene;
        }
        catch
        {
            if (scene.isCreated) scene.Dispose();
            throw;
        }
    }

    private async BTask<Scene> PrepareSceneAsync(string path, AsyncLoadContext context)
    {
        await BTask.Yield();
        context.cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var loader = services.GetService(typeof(ISceneLoader)) as ISceneLoader ??
            throw new InvalidOperationException("No ISceneLoader is registered.");
        Scene? scene = null;
        try
        {
            if (loader is IAsyncSceneLoader asynchronous) scene = await asynchronous.LoadSceneAsync(path, services, context);
            else await context.RunSteps(() => { scene = loader.LoadScene(path, services); return CooperativeWorkStatus.Complete; });
            context.cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            return scene ?? throw new InvalidOperationException($"The scene loader returned null for '{path}'.");
        }
        catch { if (scene is { isCreated: true }) scene.Dispose(); throw; }
    }

    private Scene ActivateScene(Scene scene, string sceneNameOrPath, LoadSceneMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        if (!scene.isCreated)
            throw new ObjectDisposedException(nameof(scene), "An unloaded Scene cannot be registered.");
        RegisterSceneCore(scene);
        if (setActive || _activeScene is null) SetActiveScene(scene);
    }

    public bool UnloadScene(Scene scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        if (!_loadedScenes.Contains(scene)) return false;
        if (_loadedScenes.Count == 1)
            throw new InvalidOperationException("The only loaded Scene cannot be unloaded.");

        return UnregisterScene(scene, disposeScene: true);
    }

    public SceneUnloadOperation UnloadSceneAsync(Scene scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        return Track(new SceneUnloadOperation(cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UnloadScene(scene);
        }));
    }

    public bool UnregisterScene(Scene scene, bool disposeScene = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        if (!_loadedScenes.Contains(scene)) return false;

        var previousActive = _activeScene;
        if (disposeScene && _loadedScenes.Count > 1)
        {
            [global::BEngine.SkipLoopGuard]
            global::BEngine.Scene Query7026(global::System.Collections.Generic.IEnumerable<global::BEngine.Scene> querySource7026)
            {
                ArgumentNullException.ThrowIfNull(querySource7026, "source");
                bool Matches7026(global::BEngine.Scene item) => !ReferenceEquals(item, scene);
                global::System.ReadOnlySpan<global::BEngine.Scene> queryValues7026;
                if (querySource7026.GetType() == typeof(global::BEngine.Scene[]))
                    queryValues7026 = (global::BEngine.Scene[])querySource7026;
                else if (querySource7026.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Scene>))
                    queryValues7026 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Scene>)querySource7026);
                else
                {
                    foreach (var item in querySource7026)
                        if (Matches7026(item))
                            return item;
                    return global::System.Linq.Enumerable.First(global::System.Array.Empty<global::BEngine.Scene>(), static _ => true);
                }
            
                foreach (var item in queryValues7026)
                    if (Matches7026(item))
                        return item;
                return global::System.Linq.Enumerable.First(global::System.Array.Empty<global::BEngine.Scene>(), static _ => true);
            }
            
            var persistentDestination = ReferenceEquals(_activeScene, scene) ? Gather8482(_loadedScenes) : _activeScene ?? Query7026(_loadedScenes);
            [global::BEngine.SkipLoopGuard]
            global::BEngine.Scene Gather8482(global::System.Collections.Generic.IEnumerable<global::BEngine.Scene> items8482)
            {
                ArgumentNullException.ThrowIfNull(items8482, "source");
                bool Evaluate8482(global::BEngine.Scene item) => !ReferenceEquals(item, scene);
                global::System.ReadOnlySpan<global::BEngine.Scene> values;
                if (items8482.GetType() == typeof(global::BEngine.Scene[]))
                    values = (global::BEngine.Scene[])items8482;
                else if (items8482.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Scene>))
                    values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Scene>)items8482);
                else
                    return global::System.Linq.Enumerable.Last(items8482, Evaluate8482);
                for (var index = values.Length - 1; index >= 0; index--)
                {
                    var item = values[index];
                    if (Evaluate8482(item))
                        return item;
                }

                return global::System.Linq.Enumerable.Last(global::System.Array.Empty<global::BEngine.Scene>(), static _ => true);
            }
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
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        ObjectDisposedException.ThrowIf(_disposed, this);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var operation in _pendingOperations.ToArray()) operation.Cancel();
        _pendingOperations.Clear();
        SceneLoaded = null;
        SceneUnloaded = null;
        ActiveSceneChanged = null;
        _activeScene = null;
        _loadedScenes.Clear();
    }

    private T Track<T>(T operation) where T : AsyncOperation
    {
        _pendingOperations.Add(operation);
        operation.completed += completed => _pendingOperations.Remove(completed);
        return operation;
    }

    private void UnloadSceneCore(Scene scene)
    {
        SceneRuntime.StopRunningScene(scene);
        foreach (var root in scene.rootGameObjects.ToArray()) scene.Destroy(root);
        scene.Dispose();
        scene.isLoaded = false;
        SceneUnloaded?.Invoke(scene);
    }

    private void RegisterSceneCore(Scene scene)
    {
        if (!_loadedScenes.Contains(scene)) _loadedScenes.Add(scene);
        scene.isLoaded = true;
    }
}
