namespace BEngine;

public enum HideFlags
{
    None = 0,
    HideInHierarchy = 1,
    HideInInspector = 2,
    DontSaveInEditor = 4,
    NotEditable = 8,
    DontSaveInBuild = 16,
    DontUnloadUnusedAsset = 32,
    DontSave = DontSaveInEditor | DontSaveInBuild,
    HideAndDontSave = HideInHierarchy | HideInInspector | DontSave
}

public abstract class BObject
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, WeakReference<BObject>> Objects = [];

    public Guid Id { get; internal set; } = Guid.NewGuid();
    public string name { get; set; } = string.Empty;
    public HideFlags hideFlags { get; set; }

    protected BObject() => Objects[GetInstanceID()] = new WeakReference<BObject>(this);

    public int GetInstanceID() => Id.GetHashCode();

    public static BObject? FindObjectFromInstanceID(int instanceId)
    {
        if (!Objects.TryGetValue(instanceId, out var reference)) return null;
        if (reference.TryGetTarget(out var target)) return target;
        Objects.TryRemove(instanceId, out _);
        return null;
    }

    public static T? FindFirstObjectByType<T>() where T : BObject => FindObjectsByType<T>().FirstOrDefault();

    public static T[] FindObjectsByType<T>() where T : BObject
    {
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

    public override string ToString() => string.IsNullOrWhiteSpace(name) ? GetType().Name : name;
}

[Obsolete("Use BObject as the common engine object base type.")]
public abstract class EngineObject : BObject;
