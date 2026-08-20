using BEngine.Entities;

namespace BEngine;

public sealed class Scene : BAsset
{
    private readonly List<GameObject> _gameObjects = [];
    private readonly ThreadGuardedReadOnlyList<GameObject> _gameObjectsView;
    private readonly World _world;
    private string _path = string.Empty;
    private bool _isLoaded = true;

    public IReadOnlyList<GameObject> gameObjects
    {
        get
        {
            MainThreadGuard.Ensure();
            return _gameObjectsView;
        }
    }
    public IEnumerable<GameObject> rootGameObjects
    {
        get
        {
            MainThreadGuard.Ensure();
            return _gameObjects.Where(static item => item.TransformUnchecked.ParentUnchecked is null).ToArray();
        }
    }
    public int rootCount
    {
        get
        {
            MainThreadGuard.Ensure();
            return _gameObjects.Count(static item => item.TransformUnchecked.ParentUnchecked is null);
        }
    }
    public string path
    {
        get { MainThreadGuard.Ensure(); return _path; }
        internal set => _path = value;
    }
    public bool isLoaded
    {
        get { MainThreadGuard.Ensure(); return _isLoaded; }
        internal set => _isLoaded = value;
    }
    public World world
    {
        get { MainThreadGuard.Ensure(); return _world; }
    }

    internal World WorldUnchecked => _world;

    public Scene(string name = "Untitled", IServiceProvider? services = null)
    {
        _gameObjectsView = new ThreadGuardedReadOnlyList<GameObject>(_gameObjects);
        this.name = name;
        _world = new World($"{name} World", this, services);
    }

    public GameObject CreateGameObject(string name = "GameObject")
    {
        MainThreadGuard.Ensure();
        var gameObject = new GameObject(name);
        Add(gameObject);
        return gameObject;
    }

    internal void Add(GameObject gameObject)
    {
        MainThreadGuard.Ensure();
        if (!_isLoaded || !_world.IsCreated)
            throw new InvalidOperationException("GameObjects cannot be added to an unloaded Scene.");
        if (gameObject.SceneUnchecked is not null)
        {
            throw new InvalidOperationException("GameObject already belongs to a Scene.");
        }

        var entity = _world.EntityManager.CreateEntity();
        gameObject.BindToScene(this, entity);
        _gameObjects.Add(gameObject);
        SceneRuntime.NotifyGameObjectAdded(gameObject);
    }

    internal IReadOnlyList<GameObject> ReleaseAll()
    {
        MainThreadGuard.Ensure();
        var released = _gameObjects.ToArray();
        foreach (var gameObject in released)
        {
            var entity = gameObject.EntityUnchecked;
            gameObject.UnbindFromScene();
            _world.EntityManager.DestroyEntityImmediate(entity);
        }
        _gameObjects.Clear();
        return released;
    }

    public GameObject[] GetRootGameObjects()
    {
        MainThreadGuard.Ensure();
        return _gameObjects.Where(static item => item.TransformUnchecked.ParentUnchecked is null).ToArray();
    }

    internal void SetRootSiblingIndex(GameObject gameObject, int index)
    {
        MainThreadGuard.Ensure();
        SetRootSiblingIndexUnchecked(gameObject, index);
    }

    internal int RootIndexOfUnchecked(GameObject gameObject) =>
        _gameObjects.Where(static item => item.TransformUnchecked.ParentUnchecked is null).ToList().IndexOf(gameObject);

    internal void SetRootSiblingIndexUnchecked(GameObject gameObject, int index)
    {
        if (!ReferenceEquals(gameObject.SceneUnchecked, this) ||
            gameObject.TransformUnchecked.ParentUnchecked is not null) return;
        var roots = _gameObjects.Where(item => item.TransformUnchecked.ParentUnchecked is null &&
                                               !ReferenceEquals(item, gameObject))
            .ToArray();
        index = Math.Clamp(index, 0, roots.Length);
        _gameObjects.Remove(gameObject);
        if (index >= roots.Length) _gameObjects.Add(gameObject);
        else _gameObjects.Insert(_gameObjects.IndexOf(roots[index]), gameObject);
    }

    public static void MoveGameObjectToScene(GameObject gameObject, Scene destination)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(gameObject);
        ArgumentNullException.ThrowIfNull(destination);
        if (gameObject.SceneUnchecked is not { } source)
            throw new InvalidOperationException("GameObject does not belong to a Scene.");
        if (ReferenceEquals(source, destination)) return;
        if (gameObject.TransformUnchecked.ParentUnchecked is not null)
            throw new InvalidOperationException("Only a root GameObject can be moved between Scenes.");
        if (!source._world.IsCreated)
            throw new ObjectDisposedException(nameof(source), "The source Scene has been unloaded.");
        if (!destination._world.IsCreated || !destination._isLoaded)
            throw new InvalidOperationException("The destination Scene is not loaded.");

        var hierarchy = EnumerateHierarchy(gameObject).ToArray();
        if (hierarchy.Any(item => !ReferenceEquals(item.SceneUnchecked, source)) ||
            hierarchy.Any(item => !source._gameObjects.Contains(item)))
            throw new InvalidOperationException("The GameObject hierarchy is not wholly owned by its source Scene.");

        var oldEntities = hierarchy.Select(static item => item.EntityUnchecked).ToArray();
        var newEntities = new Entity[hierarchy.Length];
        var stagedCount = 0;
        try
        {
            for (; stagedCount < hierarchy.Length; stagedCount++)
                newEntities[stagedCount] = destination._world.EntityManager.CreateEntity();
        }
        catch
        {
            for (var index = 0; index < stagedCount; index++)
                destination._world.EntityManager.DestroyEntityImmediate(newEntities[index]);
            throw;
        }

        SceneRuntime.NotifyGameObjectMoving(gameObject, source);
        var boundCount = 0;
        try
        {
            foreach (var item in hierarchy) item.UnbindFromScene();
            for (; boundCount < hierarchy.Length; boundCount++)
                hierarchy[boundCount].BindToScene(destination, newEntities[boundCount]);

            foreach (var item in hierarchy)
            {
                source._gameObjects.Remove(item);
                destination._gameObjects.Add(item);
            }
            foreach (var entity in oldEntities)
                source._world.EntityManager.DestroyEntityImmediate(entity);
        }
        catch
        {
            for (var index = 0; index < boundCount; index++) hierarchy[index].UnbindFromScene();
            for (var index = 0; index < newEntities.Length; index++)
                destination._world.EntityManager.DestroyEntityImmediate(newEntities[index]);
            for (var index = 0; index < hierarchy.Length; index++)
                hierarchy[index].RestoreSceneBinding(source, oldEntities[index]);
            SceneRuntime.NotifyGameObjectMoved(gameObject, source);
            throw;
        }

        SceneRuntime.NotifyGameObjectMoved(gameObject, destination);
    }

    public GameObject? Find(string name)
    {
        MainThreadGuard.Ensure();
        return _gameObjects.FirstOrDefault(item => item.name == name);
    }

    public GameObject? Find(Guid id)
    {
        MainThreadGuard.Ensure();
        return _gameObjects.FirstOrDefault(item => item.Id == id);
    }

    public ManagedComponentQuery<T> QueryComponents<T>() where T : class
    {
        MainThreadGuard.Ensure();
        return _world.EntityManager.QueryManagedComponents<T>();
    }

    public bool Destroy(GameObject gameObject)
    {
        MainThreadGuard.Ensure();
        if (!_gameObjects.Contains(gameObject))
        {
            return false;
        }

        foreach (var child in gameObject.TransformUnchecked.ChildrenUnchecked.ToArray())
        {
            Destroy(child.GameObjectUnchecked);
        }

        foreach (var behaviour in gameObject.GetComponents<MonoBehaviour>())
            SceneRuntime.NotifyComponentDestroying(behaviour);

        var entity = gameObject.EntityUnchecked;
        gameObject.UnbindFromScene();
        gameObject.TransformUnchecked.SetParent(null, false);
        var removed = _gameObjects.Remove(gameObject);
        _world.EntityManager.DestroyEntityImmediate(entity);
        return removed;
    }

    private static IEnumerable<GameObject> EnumerateHierarchy(GameObject root)
    {
        yield return root;
        foreach (var child in root.TransformUnchecked.ChildrenUnchecked)
        foreach (var descendant in EnumerateHierarchy(child.GameObjectUnchecked))
            yield return descendant;
    }
}
