using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;

namespace BEngine;

[EditorIcon("Icons/Assets/AssetScene.png")]
public sealed partial class Scene : BAsset, IDisposable
{
    private readonly List<GameObject> _gameObjects = [];
    private readonly Dictionary<Type, object> _componentQueries = [];
    private readonly Dictionary<Type, object> _componentQueryViews = [];
    private Renderer2D[]? _nonSpriteRenderers;
    internal long HierarchyRevision { get; private set; }
    /// <summary>Changes when hierarchy, component membership or GameObject names invalidate hierarchical lookups.</summary>
    public long hierarchyLookupVersion { get; private set; }
    internal void InvalidateHierarchy() { HierarchyRevision++; InvalidateHierarchyLookups(); }
    internal void InvalidateHierarchyLookups() => hierarchyLookupVersion++;
    internal void InvalidateComponentQueries()
    {
        _componentQueries.Clear();
        _componentQueryViews.Clear();
        _nonSpriteRenderers = null;
        InvalidateHierarchy();
    }
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

    public T? GetService<T>() where T : class => Services.GetService(typeof(T)) as T;

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return Services.GetService(serviceType);
    }

    internal IServiceProvider Services { get; }
    internal Fix64 RuntimeLoadTime { get; set; }
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
        InvalidateComponentQueries();
    }

    internal IReadOnlyList<GameObject> ReleaseAll()
    {
        var released = _gameObjects.ToArray();
        foreach (var gameObject in released) gameObject.UnbindFromScene();
        _gameObjects.Clear();
        InvalidateComponentQueries();
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
        InvalidateComponentQueries();
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
            source.InvalidateComponentQueries();
            item.RestoreSceneBinding(destination);
            destination._gameObjects.Add(item);
            destination.InvalidateComponentQueries();
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

    /// <summary>
    /// Returns a cached read-only snapshot of the scene's components of <typeparamref name="T"/>.
    /// Repeated queries reuse it until the hierarchy or component membership changes.
    /// Earlier snapshots retain their original membership.
    /// </summary>
    public IReadOnlyList<T> QueryComponents<T>() where T : class
    {
        var type = typeof(T);
        if (_componentQueryViews.TryGetValue(type, out var view)) return (IReadOnlyList<T>)view;
        var readOnly = Array.AsReadOnly(ComponentQuery<T>());
        _componentQueryViews.Add(type, readOnly);
        return readOnly;
    }

    public int GetComponents<T>(List<T> results) where T : class
    {
        ArgumentNullException.ThrowIfNull(results);
        FillComponents(results);
        return results.Count;
    }

    private T[] ComponentQuery<T>() where T : class
    {
        if (_componentQueries.TryGetValue(typeof(T), out var cached)) return (T[])cached;
        var result = new List<T>();
        foreach (var gameObject in _gameObjects)
            foreach (var component in gameObject.ComponentsSpanUnchecked)
                if (component is T typed) result.Add(typed);
        var snapshot = result.ToArray();
        _componentQueries.Add(typeof(T), snapshot);
        return snapshot;
    }

    internal void FillComponents<T>(List<T> result) where T : class
    {
        ArgumentNullException.ThrowIfNull(result);
        result.Clear(); result.AddRange(ComponentQuery<T>());
    }

    internal ReadOnlySpan<T> ComponentsSpanUnchecked<T>() where T : class => ComponentQuery<T>();

    // SpriteRenderer is sealed and uses the dedicated sprite collection path;
    // its inherited CollectGeometry method is empty. Keep a membership snapshot
    // so normal rendering need not visit all sprites again for custom geometry.
    internal ReadOnlySpan<Renderer2D> NonSpriteRenderersSpanUnchecked()
    {
        if (_nonSpriteRenderers is not null) return _nonSpriteRenderers;
        var renderers = ComponentQuery<Renderer2D>();
        var count = 0;
        foreach (var renderer in renderers)
            if (renderer is not SpriteRenderer) count++;
        var snapshot = count == 0 ? [] : new Renderer2D[count];
        var index = 0;
        foreach (var renderer in renderers)
            if (renderer is not SpriteRenderer) snapshot[index++] = renderer;
        _nonSpriteRenderers = snapshot;
        return snapshot;
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
        InvalidateComponentQueries();
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
            InvalidateComponentQueries();
            HierarchyOrder2D.Forget(this);
            _serviceScope?.Dispose();
        }
        finally
        {
            _isLoaded = false;
            _disposed = true;
            _disposing = false;
            _transformBatch.Clear(); _transformBatch.TrimExcess(); _transformTraversal.Clear();
            DestroyAsset();
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
