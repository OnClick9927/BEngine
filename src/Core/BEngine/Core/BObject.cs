using BEngine.Documents;

namespace BEngine;

public abstract class BObject
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, WeakReference<BObject>> Objects = [];
    private Guid _id = Guid.NewGuid();
    private string _name = string.Empty;
    private HideFlags _hideFlags;
    private Guid? _prefabAssetId;
    private Guid? _prefabSourceId;

    public Guid Id
    {
        get
        {
            MainThreadGuard.Ensure();
            return _id;
        }
        internal set
        {
            MainThreadGuard.Ensure();
            _id = value;
        }
    }
    public string name
    {
        get
        {
            MainThreadGuard.Ensure();
            return _name;
        }
        set
        {
            MainThreadGuard.Ensure();
            _name = value;
        }
    }
    public HideFlags hideFlags
    {
        get
        {
            MainThreadGuard.Ensure();
            return _hideFlags;
        }
        set
        {
            MainThreadGuard.Ensure();
            _hideFlags = value;
        }
    }

    // Prefab linkage is engine metadata, not a user-serialized component field.
    internal Guid? PrefabAssetId
    {
        get
        {
            MainThreadGuard.Ensure();
            return _prefabAssetId;
        }
        set
        {
            MainThreadGuard.Ensure();
            _prefabAssetId = value;
        }
    }
    internal Guid? PrefabSourceId
    {
        get
        {
            MainThreadGuard.Ensure();
            return _prefabSourceId;
        }
        set
        {
            MainThreadGuard.Ensure();
            _prefabSourceId = value;
        }
    }

    protected BObject()
    {
        MainThreadGuard.Ensure("Create BObject");
        Objects[GetInstanceID()] = new WeakReference<BObject>(this);
    }

    public int GetInstanceID()
    {
        MainThreadGuard.Ensure();
        return _id.GetHashCode();
    }

    public static BObject? FindObjectFromInstanceID(int instanceId)
    {
        MainThreadGuard.Ensure();
        if (!Objects.TryGetValue(instanceId, out var reference)) return null;
        if (reference.TryGetTarget(out var target)) return target;
        Objects.TryRemove(instanceId, out _);
        return null;
    }

    public static T? FindFirstObjectByType<T>() where T : BObject => FindObjectsByType<T>().FirstOrDefault();
    public static T? FindAnyObjectByType<T>() where T : BObject => FindFirstObjectByType<T>();

    public static T[] FindObjectsByType<T>() where T : BObject
    {
        MainThreadGuard.Ensure();
        var results = new List<T>();
        foreach (var pair in Objects.ToArray())
        {
            if (!pair.Value.TryGetTarget(out var target))
            {
                Objects.TryRemove(pair.Key, out _);
                continue;
            }
            if (target is T typed) results.Add(typed);
        }
        return [.. results];
    }

    public static void Destroy(BObject target)
    {
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(target);
        switch (target)
        {
            case GameObject gameObject when gameObject.scene is { } scene:
                scene.Destroy(gameObject);
                break;
            case Component component:
                component.gameObject.RemoveComponent(component);
                break;
        }
    }

    public static void Destroy(BObject target, Fix64 delay)
    {
        MainThreadGuard.Ensure();
        if (delay <= Fix64.Zero) Destroy(target);
        else DelayedDestroy.Schedule(target, delay);
    }

    public static void DestroyImmediate(BObject target, bool allowDestroyingAssets = false) => Destroy(target);

    public static void DontDestroyOnLoad(BObject target)
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
        MainThreadGuard.Ensure();
        ArgumentNullException.ThrowIfNull(original);
        return original switch
        {
            GameObject gameObject => CloneGameObject(gameObject, null),
            Component component => CloneComponent(component),
            ScriptableObject scriptable => CloneScriptableObject(scriptable),
            Material material => CloneMaterial(material),
            _ => throw new NotSupportedException($"Instantiate does not support {original.GetType().FullName}.")
        };
    }

    private static GameObject CloneGameObject(GameObject source, Transform? parent)
    {
        var clone = new GameObject(source.name)
        {
            activeSelf = source.activeSelf,
            tag = source.tag,
            layer = source.layer,
            isStatic = source.isStatic,
            hideFlags = source.hideFlags,
            PrefabAssetId = source.PrefabAssetId,
            PrefabSourceId = source.PrefabSourceId
        };
        if (source.scene is { } scene) scene.Add(clone);
        clone.transform.localPosition = source.transform.localPosition;
        clone.transform.localRotation = source.transform.localRotation;
        clone.transform.localScale = source.transform.localScale;
        clone.transform.PrefabAssetId = source.transform.PrefabAssetId;
        clone.transform.PrefabSourceId = source.transform.PrefabSourceId;
        if (parent is not null) clone.transform.SetParent(parent, false);
        foreach (var component in source.components.Where(component => component is not Transform))
        {
            var copied = clone.AddComponent(component.GetType());
            ComponentFieldSerializer.Deserialize(copied, ComponentFieldSerializer.Serialize(component));
            copied.enabled = component.enabled;
            copied.PrefabAssetId = component.PrefabAssetId;
            copied.PrefabSourceId = component.PrefabSourceId;
        }
        foreach (var child in source.transform.children) CloneGameObject(child.gameObject, clone.transform);
        return clone;
    }

    private static Component CloneComponent(Component source)
    {
        var cloneRoot = CloneGameObject(source.gameObject, null);
        return cloneRoot.GetComponent(source.GetType()) ??
               throw new InvalidOperationException($"Could not clone component {source.GetType().Name}.");
    }

    private static ScriptableObject CloneScriptableObject(ScriptableObject source)
    {
        var clone = ScriptableObject.CreateInstance(source.GetType());
        clone.name = source.name;
        clone.hideFlags = source.hideFlags;
        foreach (var member in RuntimeTypeCache.GetInstanceMembers(source.GetType()))
        {
            if (member.DeclaringType == typeof(BObject)) continue;
            if (member is System.Reflection.FieldInfo field &&
                (field.IsStatic || field.IsInitOnly || !field.IsPublic &&
                 !field.IsDefined(typeof(SerializeFieldAttribute), inherit: true))) continue;
            if (member is System.Reflection.PropertyInfo property &&
                (property.GetMethod?.IsPublic is not true || property.SetMethod?.IsPublic is not true)) continue;
            var accessor = RuntimeTypeCache.GetMemberAccessor(member);
            if (accessor.Setter is not null) accessor.Setter(clone, accessor.Getter(source));
        }
        return clone;
    }

    private static Material CloneMaterial(Material source)
    {
        var clone = new Material(source.shader) { name = source.name, hideFlags = source.hideFlags };
        clone.CopyPropertiesFromMaterial(source);
        return clone;
    }

    public override string ToString() => string.IsNullOrWhiteSpace(name) ? GetType().Name : name;
}
