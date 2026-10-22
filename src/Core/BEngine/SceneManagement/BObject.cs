using BEngine.Serialization;
using BEngine.SceneManagement;
using YamlDotNet.Serialization;

namespace BEngine;

public abstract class BObject
{
    private static readonly object ObjectsGate = new();
    private static readonly Dictionary<int, WeakReference<BObject>> Objects = [];
    private static int _nextInstanceId;
    private readonly int _instanceId;
    private Guid _id = Guid.NewGuid();
    private string _name = string.Empty;
    private HideFlags _hideFlags;
    private Guid? _prefabAssetId;
    private Guid? _prefabSourceId;

    public Guid Id
    {
        get
        {
            return _id;
        }
        internal set
        {
            _id = value;
        }
    }
    public string name
    {
        get
        {
            return _name;
        }
        set
        {
            if (_name == value) return;
            _name = value;
            if (this is GameObject owner) owner.SceneUnchecked?.InvalidateHierarchyLookups();
        }
    }
    public HideFlags hideFlags
    {
        get
        {
            return _hideFlags;
        }
        set
        {
            _hideFlags = value;
        }
    }

    // Prefab linkage is engine metadata, not a user-serialized component field.
    internal Guid? PrefabAssetId
    {
        get
        {
            return _prefabAssetId;
        }
        set
        {
            _prefabAssetId = value;
        }
    }
    internal Guid? PrefabSourceId
    {
        get
        {
            return _prefabSourceId;
        }
        set
        {
            _prefabSourceId = value;
        }
    }

    [YamlIgnore]
    internal bool IsRuntimeOnly { get; set; }

    protected BObject()
    {
        _instanceId = NextInstanceId();
        IsRuntimeOnly = Application.isPlaying && SceneRuntime.currentScene is not null;
        Register(this);
    }

    [YamlIgnore, HideInInspector]
    public bool isDestroyed { get; private set; }

    /// <summary>Raised once when this object is destroyed. Subscribers are released afterwards.</summary>
    public event Action<BObject>? destroyed;

    public int GetInstanceID() => _instanceId;

    public static BObject? FindObjectFromInstanceID(int instanceId)
    {
        BObject target;
        lock (ObjectsGate)
        {
            if (!Objects.TryGetValue(instanceId, out var reference)) return null;
            if (!reference.TryGetTarget(out target!))
            {
                Objects.Remove(instanceId);
                return null;
            }
        }
        return IsVisibleInCurrentRuntimeDomain(target) ? target : null;
    }

    public static T? FindFirstObjectByType<T>() where T : BObject
    {
        // Resolve service/domain snapshots before entering the registry lock. Service
        // callbacks may register objects; never invoke them while enumerating Objects.
        Scene[]? visibleScenes = null;
        if (SceneRuntime.currentScene is { } current)
        {
            visibleScenes = current.Services.GetService(typeof(IRuntimeSceneManager)) is IRuntimeSceneManager manager
                ? manager.LoadedScenes.ToArray() : [current];
            if (!visibleScenes.Contains(current)) visibleScenes = [current];
        }
        lock (ObjectsGate)
        {
            List<int>? stale = null;
            try
            {
                foreach (var pair in Objects)
                {
                    if (!pair.Value.TryGetTarget(out var target)) { (stale ??= []).Add(pair.Key); continue; }
                    if (target is not T typed || target.isDestroyed) continue;
                    if (visibleScenes is null) return typed;
                    Scene? scene;
                    switch (target)
                    {
                        case Scene value: scene = value; break;
                        case GameObject value: scene = value.SceneUnchecked; break;
                        case Component value:
                            try { scene = value.GameObjectUnchecked.SceneUnchecked; }
                            catch (InvalidOperationException) { continue; }
                            break;
                        default: return typed;
                    }
                    if (scene is not null && visibleScenes.Contains(scene)) return typed;
                }
            }
            finally { if (stale is not null) foreach (var id in stale) Objects.Remove(id); }
        }
        return null;
    }

    public static T? FindAnyObjectByType<T>() where T : BObject => FindFirstObjectByType<T>();

    public static T[] FindObjectsByType<T>() where T : BObject
    {
        var results = GetRegisteredObjectsByType<T>();
        results.RemoveAll(target => !IsVisibleInCurrentRuntimeDomain(target));
        return [.. results];
    }

    private static List<T> GetRegisteredObjectsByType<T>() where T : BObject
    {
        // Loading threads register objects while editor views enumerate them. Capture live
        // references under the registry lock, then consult scene services outside the lock.
        var results = new List<T>();
        lock (ObjectsGate)
        {
            List<int>? staleIds = null;
            foreach (var pair in Objects)
            {
                if (!pair.Value.TryGetTarget(out var target))
                {
                    (staleIds ??= []).Add(pair.Key);
                    continue;
                }
                if (target is T typed) results.Add(typed);
            }
            if (staleIds is not null)
                foreach (var instanceId in staleIds) Objects.Remove(instanceId);
        }
        return results;
    }

    private static bool IsVisibleInCurrentRuntimeDomain(BObject target)
    {
        if (target.isDestroyed) return false;
        if (SceneRuntime.currentScene is null) return true;

        Scene? scene;
        switch (target)
        {
            case Scene value:
                scene = value;
                break;
            case GameObject gameObject:
                scene = gameObject.SceneUnchecked;
                break;
            case Component component:
                try { scene = component.GameObjectUnchecked.SceneUnchecked; }
                catch (InvalidOperationException) { return false; }
                break;
            default:
                return true;
        }

        return scene is not null && SceneRuntime.IsSceneVisibleInCurrentRuntimeDomain(scene);
    }

    private static int NextInstanceId()
    {
        var id = Interlocked.Increment(ref _nextInstanceId);
        return id != 0 ? id : Interlocked.Increment(ref _nextInstanceId);
    }

    internal static void Register(BObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (ObjectsGate)
        {
            target.isDestroyed = false;
            Objects[target._instanceId] = new WeakReference<BObject>(target);
        }
    }

    internal static void Unregister(BObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (ObjectsGate)
        {
            if (target.isDestroyed) return;
            Objects.Remove(target._instanceId);
            target.isDestroyed = true;
        }
        var callbacks = target.destroyed;
        target.destroyed = null;
        if (callbacks is null) return;
        foreach (Action<BObject> callback in Delegate.EnumerateInvocationList(callbacks))
            RuntimeLifecycle.Invoke(callback, target, nameof(destroyed));
    }

    public static void Destroy(BObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.isDestroyed) return;
        switch (target)
        {
            case Scene assetScene:
                assetScene.Dispose();
                break;
            case GameObject gameObject when gameObject.scene is { } scene:
                scene.Destroy(gameObject);
                break;
            case GameObject gameObject:
                gameObject.DestroyDetachedUnchecked();
                break;
            case Component component:
                if (component.GameObjectOrNull is { } owner) owner.RemoveComponent(component);
                else Unregister(component);
                break;
            case BAsset asset:
                asset.DestroyAsset();
                break;
            default:
                Unregister(target);
                break;
        }
    }

    public static void Destroy(BObject target, Fix64 delay)
    {
        if (delay <= Fix64.Zero) Destroy(target);
        else DelayedDestroy.Schedule(target, delay);
    }

    public static void DestroyImmediate(BObject target, bool allowDestroyingAssets = false) => Destroy(target);

    public static void DontDestroyOnLoad(BObject target)
    {
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

    public static T Instantiate<T>(T original) where T : BObject => (T)Instantiate((BObject)original);
    public static T Instantiate<T>(T original, Vector2 position, Fix64 rotation) where T : BObject
    {
        var clone = Instantiate(original);
        var transform = clone switch { GameObject gameObject => gameObject.transform, Component component => component.transform, _ => null };
        if (transform is not null) transform.SetPositionAndRotation(position, rotation);
        return clone;
    }

    public static BObject Instantiate(BObject original)
    {
        ArgumentNullException.ThrowIfNull(original);
        ObjectDisposedException.ThrowIf(original.isDestroyed, original);
        return original switch
        {
            GameObject gameObject => CloneGameObject(gameObject, null),
            Component component => CloneComponent(component),
            ScriptableObject scriptable => CloneScriptableObject(scriptable),
            Material material => CloneMaterial(material),
            _ => throw new NotSupportedException($"Instantiate does not support {original.GetType().FullName}.")
        };
    }

    private static GameObject CloneGameObject(GameObject source, Transform? parent) =>
        CloneGameObject(source, parent, out _);

    private static GameObject CloneGameObject(
        GameObject source, Transform? parent, out Dictionary<Guid, BObject> objectMap)
    {
        var map = new Dictionary<Guid, BObject>();
        objectMap = map;
        var components = new List<(Component Source, Component Clone)>();
        var createdObjects = new List<GameObject>();
        try
        {
            var clone = CloneHierarchy(source, parent, map, components, createdObjects);
            BObject? Resolve(Guid id) => map.GetValueOrDefault(id) ?? FindSceneObject(source.scene, id);
            foreach (var (sourceComponent, clonedComponent) in components)
            {
                SerializationCallbackUtility.BeforeSerialize(sourceComponent);
                ComponentObjectGraphSerializer.Restore(clonedComponent,
                    ComponentObjectGraphSerializer.Capture(sourceComponent, allowTransientObjects: true), Resolve);
                SerializationCallbackUtility.AfterDeserialize(clonedComponent);
            }
            var prefabIds = map.ToDictionary(pair => pair.Key, pair => pair.Value.Id);
            foreach (var original in PrefabAssetOperations.Traverse(source))
                if (original.PrefabInstance is { } reference && map[original.Id] is GameObject instance)
                    instance.PrefabInstance = PrefabResolver.RemapReference(reference, prefabIds);
            if (parent is null && source.scene is { } scene) scene.AddHierarchy(clone);
            return clone;
        }
        catch
        {
            // The allocation may fail before the root or a child reaches the identity map.
            // Only roll back objects allocated by this clone, never borrowed serialized references.
            for (var index = createdObjects.Count - 1; index >= 0; index--)
            {
                var created = createdObjects[index];
                if (created.isDestroyed) continue;
                if (created.SceneUnchecked is { } scene) scene.Destroy(created);
                else
                {
                    // An unpublished clone has never entered a scene lifecycle. In particular,
                    // OnDestroy must not run against partly restored, borrowed asset references.
                    created.TransformUnchecked.SetParent(null, false);
                    created.UnregisterObjectNodeUnchecked();
                }
            }
            throw;
        }
    }

    private static GameObject CloneHierarchy(
        GameObject source,
        Transform? parent,
        IDictionary<Guid, BObject> objectMap,
        ICollection<(Component Source, Component Clone)> components,
        ICollection<GameObject> createdObjects)
    {
        var clone = new GameObject(source.name);
        createdObjects.Add(clone);
        clone.activeSelf = source.activeSelf;
        clone.tag = source.tag;
        clone.layer = source.layer;
        clone.isStatic = source.isStatic;
        clone.hideFlags = source.hideFlags;
        clone.PrefabAssetId = source.PrefabAssetId;
        clone.PrefabSourceId = source.PrefabSourceId;
        if (source.transform.GetType() != typeof(Transform)) clone.AddComponent(source.transform.GetType());
        clone.transform.localPosition = source.transform.localPosition;
        clone.transform.localRotation = source.transform.localRotation;
        clone.transform.localScale = source.transform.localScale;
        clone.transform.PrefabAssetId = source.transform.PrefabAssetId;
        clone.transform.PrefabSourceId = source.transform.PrefabSourceId;
        if (parent is not null) clone.transform.SetParent(parent, false);
        objectMap.Add(source.Id, clone);
        objectMap.Add(source.transform.Id, clone.transform);
        foreach (var component in source.components.Where(component => component is not Transform))
        {
            var copied = clone.AddComponent(component.GetType());
            copied.enabled = component.enabled;
            copied.PrefabAssetId = component.PrefabAssetId;
            copied.PrefabSourceId = component.PrefabSourceId;
            objectMap.Add(component.Id, copied);
            components.Add((component, copied));
        }
        foreach (var child in source.transform.children)
            CloneHierarchy(child.gameObject, clone.transform, objectMap, components, createdObjects);
        return clone;
    }

    private static BObject? FindSceneObject(Scene? scene, Guid id)
    {
        if (scene is null) return null;
        foreach (var gameObject in scene.gameObjects)
        {
            if (gameObject.Id == id) return gameObject;
            foreach (var component in gameObject.components)
                if (component.Id == id) return component;
        }
        return null;
    }

    private static Component CloneComponent(Component source)
    {
        CloneGameObject(source.gameObject, null, out var objectMap);
        return objectMap.TryGetValue(source.Id, out var clone) && clone is Component component
            ? component
            : throw new InvalidOperationException($"Could not clone component {source.GetType().Name}.");
    }

    private static ScriptableObject CloneScriptableObject(ScriptableObject source)
    {
        var clone = ScriptableObject.CreateInstance(source.GetType());
        clone.name = source.name;
        clone.hideFlags = source.hideFlags;
        try
        {
            SerializationCallbackUtility.BeforeSerialize(source);
            ComponentObjectGraphSerializer.CopySerializedState(source, clone);
            clone.CopyClonePayloadFrom(source, value => ReferenceEquals(value, source) ? clone : value);
            SerializationCallbackUtility.AfterDeserialize(clone);
            return clone;
        }
        catch
        {
            DestroyImmediate(clone);
            throw;
        }
    }

    private static Material CloneMaterial(Material source)
    {
        var clone = new Material(source.shader) { name = source.name, hideFlags = source.hideFlags };
        clone.CopyPropertiesFromMaterial(source);
        return clone;
    }

    public override string ToString() => string.IsNullOrWhiteSpace(name) ? GetType().Name : name;
}
