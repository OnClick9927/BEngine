using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;

namespace BEngine;

[EditorIcon("Icons/Assets/AssetScene.png")]
public sealed class Scene : BAsset, IDisposable
{
    private readonly List<GameObject> _gameObjects = [];
    private readonly IReadOnlyList<GameObject> _gameObjectsView;
    private readonly IServiceScope? _serviceScope;
    private string _path = string.Empty;
    private bool _isLoaded = true;
    private bool _disposing;
    private bool _disposed;

    public IReadOnlyList<GameObject> gameObjects => _gameObjectsView;
    public IEnumerable<GameObject> rootGameObjects => GetRootGameObjects();
    public int rootCount
    {
        get
        {
            var count = 0;
            foreach (var gameObject in _gameObjects)
                if (gameObject.TransformUnchecked.ParentUnchecked is null) count++;
            return count;
        }
    }
    public string path
    {
        get => _path;
        internal set => _path = value;
    }
    public bool isLoaded
    {
        get => _isLoaded;
        internal set => _isLoaded = value;
    }
    public bool isCreated => !_disposed;

    internal IServiceProvider Services { get; }
    internal ReadOnlySpan<GameObject> GameObjectsSpanUnchecked => CollectionsMarshal.AsSpan(_gameObjects);

    public Scene(string name = "Untitled", IServiceProvider? services = null)
    {
        _gameObjectsView = _gameObjects.AsReadOnly();
        this.name = name;
        if (services?.GetService(typeof(IServiceScopeFactory)) is IServiceScopeFactory scopeFactory)
        {
            _serviceScope = scopeFactory.CreateScope();
            Services = _serviceScope.ServiceProvider;
        }
        else
            Services = services ?? EmptyServiceProvider.Instance;
    }

    public GameObject CreateGameObject(string name = "GameObject")
    {
        ThrowIfDisposed();
        var gameObject = new GameObject(name);
        Add(gameObject);
        return gameObject;
    }

    internal void Add(GameObject gameObject)
    {
        ThrowIfDisposed();
        if (!_isLoaded)
            throw new InvalidOperationException("GameObjects cannot be added to an unloaded Scene.");
        if (gameObject.SceneUnchecked is not null)
            throw new InvalidOperationException("GameObject already belongs to a Scene.");

        BindGameObject(gameObject);
        SceneRuntime.NotifyGameObjectAdded(gameObject);
    }

    internal void AddHierarchy(GameObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        ThrowIfDisposed();
        if (!_isLoaded)
            throw new InvalidOperationException("GameObjects cannot be added to an unloaded Scene.");

        var hierarchy = EnumerateHierarchy(root).ToArray();
        if (hierarchy.Any(gameObject => gameObject.SceneUnchecked is not null))
            throw new InvalidOperationException("GameObject hierarchy already belongs to a Scene.");

        foreach (var gameObject in hierarchy) BindGameObject(gameObject);
        SceneRuntime.NotifyGameObjectAdded(root);
    }

    private void BindGameObject(GameObject gameObject)
    {
        if (IsRuntimeOnly)
        {
            gameObject.IsRuntimeOnly = true;
            foreach (var component in gameObject.ComponentsSpanUnchecked)
                component.IsRuntimeOnly = true;
        }
        gameObject.BindToScene(this);
        gameObject.RegisterObjectNodeUnchecked();
        _gameObjects.Add(gameObject);
    }

    internal IReadOnlyList<GameObject> ReleaseAll()
    {
        var released = _gameObjects.ToArray();
        foreach (var gameObject in released) gameObject.UnbindFromScene();
        _gameObjects.Clear();
        return released;
    }

    public GameObject[] GetRootGameObjects()
    {
        var result = new GameObject[rootCount];
        var index = 0;
        foreach (var gameObject in _gameObjects)
            if (gameObject.TransformUnchecked.ParentUnchecked is null) result[index++] = gameObject;
        return result;
    }

    internal void SetRootSiblingIndex(GameObject gameObject, int index) =>
        SetRootSiblingIndexUnchecked(gameObject, index);

    internal int RootIndexOfUnchecked(GameObject gameObject)
    {
        var rootIndex = 0;
        foreach (var candidate in _gameObjects)
        {
            if (candidate.TransformUnchecked.ParentUnchecked is not null) continue;
            if (ReferenceEquals(candidate, gameObject)) return rootIndex;
            rootIndex++;
        }
        return -1;
    }

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
        ArgumentNullException.ThrowIfNull(gameObject);
        ArgumentNullException.ThrowIfNull(destination);
        if (gameObject.SceneUnchecked is not { } source)
            throw new InvalidOperationException("GameObject does not belong to a Scene.");
        if (ReferenceEquals(source, destination)) return;
        if (gameObject.TransformUnchecked.ParentUnchecked is not null)
            throw new InvalidOperationException("Only a root GameObject can be moved between Scenes.");
        source.ThrowIfDisposed();
        destination.ThrowIfDisposed();
        if (!destination._isLoaded)
            throw new InvalidOperationException("The destination Scene is not loaded.");

        var hierarchy = EnumerateHierarchy(gameObject).ToArray();
        if (hierarchy.Any(item => !ReferenceEquals(item.SceneUnchecked, source)) ||
            hierarchy.Any(item => !source._gameObjects.Contains(item)))
            throw new InvalidOperationException("The GameObject hierarchy is not wholly owned by its source Scene.");

        SceneRuntime.NotifyGameObjectMoving(gameObject, source);
        foreach (var item in hierarchy)
        {
            source._gameObjects.Remove(item);
            item.RestoreSceneBinding(destination);
            destination._gameObjects.Add(item);
        }
        SceneRuntime.NotifyGameObjectMoved(gameObject, destination);
    }

    public GameObject? Find(string name)
    {
        foreach (var gameObject in _gameObjects)
            if (gameObject.name == name) return gameObject;
        return null;
    }

    public GameObject? Find(Guid id)
    {
        foreach (var gameObject in _gameObjects)
            if (gameObject.Id == id) return gameObject;
        return null;
    }

    public IReadOnlyList<T> QueryComponents<T>() where T : class
    {
        var result = new List<T>();
        foreach (var gameObject in _gameObjects)
            foreach (var component in gameObject.ComponentsSpanUnchecked)
                if (component is T typed) result.Add(typed);
        return result.Count == 0 ? [] : [.. result];
    }

    public int GetComponents<T>(List<T> results) where T : class
    {
        ArgumentNullException.ThrowIfNull(results);
        FillComponents(results);
        return results.Count;
    }

    internal void FillComponents<T>(List<T> result) where T : class
    {
        ArgumentNullException.ThrowIfNull(result);
        result.Clear();
        foreach (var gameObject in _gameObjects)
            foreach (var component in gameObject.ComponentsSpanUnchecked)
                if (component is T typed) result.Add(typed);
    }

    internal bool Contains(GameObject gameObject) => _gameObjects.Contains(gameObject);

    internal void MarkRuntimeOnly()
    {
        IsRuntimeOnly = true;
        foreach (var gameObject in _gameObjects)
        {
            gameObject.IsRuntimeOnly = true;
            foreach (var component in gameObject.ComponentsSpanUnchecked)
                component.IsRuntimeOnly = true;
        }
    }

    public bool Destroy(GameObject gameObject)
    {
        if (!_gameObjects.Contains(gameObject) || !gameObject.TryBeginDestroyUnchecked()) return false;

        foreach (var child in gameObject.TransformUnchecked.ChildrenUnchecked.ToArray())
            Destroy(child.GameObjectUnchecked);
        foreach (var behaviour in gameObject.GetComponents<MonoBehaviour>())
            SceneRuntime.NotifyComponentDestroying(behaviour);

        gameObject.UnbindFromScene();
        gameObject.TransformUnchecked.SetParent(null, false);
        if (!_gameObjects.Remove(gameObject)) return false;
        gameObject.UnregisterObjectNodeUnchecked();
        return true;
    }

    public void Dispose()
    {
        if (_disposed || _disposing) return;
        _disposing = true;
        try
        {
            SceneRuntime.StopRunningScene(this);
            foreach (var root in rootGameObjects.ToArray()) Destroy(root);
            _gameObjects.Clear();
            _serviceScope?.Dispose();
        }
        finally
        {
            _isLoaded = false;
            _disposed = true;
            _disposing = false;
            BObject.Unregister(this);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Scene), "The Scene has been unloaded.");
    }

    private static IEnumerable<GameObject> EnumerateHierarchy(GameObject root)
    {
        yield return root;
        foreach (var child in root.TransformUnchecked.ChildrenUnchecked)
        foreach (var descendant in EnumerateHierarchy(child.GameObjectUnchecked))
            yield return descendant;
    }
}
