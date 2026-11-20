
namespace BEngine.Editor;

public static class Selection
{
    private static Guid? _lastSelection;
    private static int[] _lastObjects = [];
    private static BObject[] _objects = [];
    private static BObject? _activeContext;

    public static event Action? selectionChanged;

    public static BObject? activeObject
    {
        get
        {
            if (EditorBridge.Host?.ActiveObject is { isDestroyed: false } active) return active;
            foreach (var item in _objects)
                if (!item.isDestroyed) return item;
            return null;
        }
        set
        {
            if (value?.isDestroyed == true) value = null;
            _objects = value is null ? [] : [value];
            if (EditorBridge.Host is { } host)
            {
                host.ActiveObject = value;
            }
            NotifyHostSelectionChanged(value);
        }
    }

    public static GameObject? activeGameObject
    {
        get => activeObject is { } active ? ToGameObject(active) : null;
        set => activeObject = value;
    }

    public static Transform? activeTransform => activeGameObject?.transform;
    public static BObject? activeContext { get => _activeContext; set => _activeContext = value; }
    public static BObject[] objects
    {
        get
        {
            var length = count;
            if (length == 0) return [];
            var result = new BObject[length];
            var index = 0;
            foreach (var item in _objects)
                if (!item.isDestroyed) result[index++] = item;
            return result;
        }
        set
        {
            _objects = value?.Where(item => item is { isDestroyed: false })
                .DistinctBy(item => item.GetInstanceID()).ToArray() ?? [];
            var first = _objects.FirstOrDefault();
            if (EditorBridge.Host is { } host) host.ActiveObject = first;
            NotifyHostSelectionChanged(first);
        }
    }
    public static GameObject[] gameObjects => GetGameObjectSelection(static item => item);
    public static Transform[] transforms => GetGameObjectSelection(static item => item.transform);
    public static int count
    {
        get
        {
            var result = 0;
            foreach (var item in _objects) if (!item.isDestroyed) result++;
            return result;
        }
    }
    public static int activeInstanceID => activeObject?.GetInstanceID() ?? 0;
    public static int[] instanceIDs
    {
        get
        {
            var length = count;
            if (length == 0) return [];
            var result = new int[length];
            var index = 0;
            foreach (var item in _objects)
                if (!item.isDestroyed) result[index++] = item.GetInstanceID();
            return result;
        }
        set => objects = value?.Select(BObject.FindObjectFromInstanceID)
            .OfType<BObject>().ToArray() ?? [];
    }

    public static bool Contains(BObject? target)
    {
        if (target is null || target.isDestroyed) return false;
        foreach (var item in _objects) if (ReferenceEquals(item, target)) return true;
        return false;
    }
    public static bool Contains(int instanceId)
    {
        foreach (var item in _objects)
            if (!item.isDestroyed && item.GetInstanceID() == instanceId) return true;
        return false;
    }

    private static T[] GetGameObjectSelection<T>(Func<GameObject, T> selector)
    {
        var objects = _objects;
        if (objects.Length == 0) return [];
        if (objects.Length == 1)
            return !objects[0].isDestroyed && ToGameObject(objects[0]) is { isDestroyed: false } owner
                ? [selector(owner)] : [];
        if (objects.Length <= 8) return GetSmallGameObjectSelection(objects, selector);
        var result = new List<T>(objects.Length);
        var seen = new HashSet<Guid>();
        foreach (var item in objects)
            if (!item.isDestroyed && ToGameObject(item) is { isDestroyed: false } gameObject && seen.Add(gameObject.Id))
                result.Add(selector(gameObject));
        return result.ToArray();
    }

    private static T[] GetSmallGameObjectSelection<T>(BObject[] objects, Func<GameObject, T> selector)
    {
        // Keep the bounded small-selection workspace on the stack. Large selections
        // retain hash-based deduplication rather than paying quadratic lookup cost.
        Span<Guid> seen = stackalloc Guid[8];
        Span<int> indices = stackalloc int[8];
        var count = 0;
        for (var index = 0; index < objects.Length; index++)
        {
            var item = objects[index];
            if (item.isDestroyed || ToGameObject(item) is not { isDestroyed: false } owner ||
                seen[..count].Contains(owner.Id)) continue;
            seen[count] = owner.Id;
            indices[count++] = index;
        }
        if (count == 0) return [];
        var result = new T[count];
        for (var index = 0; index < count; index++)
            result[index] = selector(ToGameObject(objects[indices[index]])!);
        return result;
    }
    public static T[] GetFiltered<T>(SelectionMode mode = SelectionMode.Unfiltered) =>
        GetFiltered(typeof(T), mode).OfType<T>().ToArray();

    public static BObject[] GetFiltered(Type type, SelectionMode mode = SelectionMode.Unfiltered)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!typeof(BObject).IsAssignableFrom(type) && !type.IsInterface)
            throw new ArgumentException("Selection types must be engine objects or component interfaces.", nameof(type));

        var candidates = GetCandidates(mode).ToArray();
        var componentType = typeof(Component).IsAssignableFrom(type) || type.IsInterface;
        if (!componentType && !typeof(GameObject).IsAssignableFrom(type))
            return candidates.Where(type.IsInstanceOfType).Where(item => IsIncluded(item, mode))
                .DistinctBy(item => item.GetInstanceID()).ToArray();

        var roots = candidates.Select(ToGameObject).OfType<GameObject>()
            .Where(item => !item.isDestroyed).DistinctBy(item => item.GetInstanceID()).ToArray();
        var selected = roots.ToHashSet();
        var result = new List<BObject>();
        var visited = new HashSet<GameObject>();
        var pending = new Stack<GameObject>();
        foreach (var root in roots)
        {
            if (mode.HasFlag(SelectionMode.TopLevel) && HasSelectedAncestor(root, selected)) continue;
            pending.Push(root);
            while (pending.TryPop(out var item))
            {
                if (item.isDestroyed || !visited.Add(item)) continue;
                if (IsIncluded(item, mode))
                {
                    BObject? match = componentType ? item.GetComponent(type) : item;
                    if (match is not null && IsIncluded(match, mode)) result.Add(match);
                }
                if (!mode.HasFlag(SelectionMode.Deep)) continue;
                for (var index = item.transform.childCount - 1; index >= 0; index--)
                    pending.Push(item.transform.GetChild(index).gameObject);
            }
        }
        return result.ToArray();
    }

    public static Transform[] GetTransforms(SelectionMode mode) => GetFiltered<Transform>(mode);

    private static IEnumerable<BObject> GetCandidates(SelectionMode mode)
    {
        foreach (var item in objects)
        {
            if (!mode.HasFlag(SelectionMode.Assets) || AssetDatabase.Contains(item)) yield return item;
            if (!mode.HasFlag(SelectionMode.DeepAssets)) continue;
            var path = AssetDatabase.GetAssetPath(item);
            if (path.Length == 0 || !AssetDatabase.IsValidFolder(path)) continue;
            var prefix = path.Replace('\\', '/').TrimEnd('/') + "/";
            foreach (var assetPath in AssetDatabase.GetAllAssetPaths())
            {
                if (!assetPath.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) is { isDestroyed: false } asset) yield return asset;
            }
        }
    }

    private static bool IsIncluded(BObject item, SelectionMode mode) =>
        !item.isDestroyed &&
        (!mode.HasFlag(SelectionMode.Editable) || (item.hideFlags & HideFlags.NotEditable) == 0) &&
        (!mode.HasFlag(SelectionMode.ExcludePrefab) || !PrefabUtility.IsPartOfPrefabAsset(item));

    private static GameObject? ToGameObject(BObject item) => item switch
    {
        GameObject gameObject => gameObject,
        Component component when component.TryGetGameObject(out var owner) => owner,
        _ => null
    };

    private static bool HasSelectedAncestor(GameObject item, HashSet<GameObject> selected)
    {
        for (var parent = item.transform.parent; parent is not null; parent = parent.parent)
            if (selected.Contains(parent.gameObject)) return true;
        return false;
    }

    internal static void NotifyHostSelectionChanged(BObject? selected, bool force = false)
    {
        if (selected?.isDestroyed == true) selected = null;
        foreach (var item in _objects)
            if (item.isDestroyed) { _objects = objects; break; }
        if (selected is not null && !Contains(selected)) _objects = [selected];
        if (selected is null) _objects = [];
        var id = selected?.Id;
        if (!force && _lastSelection == id && MatchesLastObjects()) return;
        _lastSelection = id;
        _lastObjects = instanceIDs;
        EditorCallbacks.Dispatch(selectionChanged, nameof(selectionChanged));
    }

    private static bool MatchesLastObjects()
    {
        if (_lastObjects.Length != _objects.Length) return false;
        for (var index = 0; index < _objects.Length; index++)
            if (_lastObjects[index] != _objects[index].GetInstanceID()) return false;
        return true;
    }

    internal static void RestoreHostSelection(
        IEnumerable<BObject> objects,
        BObject? activeContext,
        BObject? activeObject)
    {
        ArgumentNullException.ThrowIfNull(objects);
        _objects = objects.Where(item => item is { isDestroyed: false }).DistinctBy(item => item.GetInstanceID()).ToArray();
        if (activeObject is { isDestroyed: false } && !Query10321())
            _objects = [activeObject, .._objects];
        bool Query10321()
        {
            var querySource10321 = _objects;
            ArgumentNullException.ThrowIfNull(querySource10321, "source");
            if (querySource10321.GetType() == typeof(global::BEngine.BObject[]) || querySource10321.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.BObject>))
            {
                global::System.ReadOnlySpan<global::BEngine.BObject> queryValues10321 = querySource10321.GetType() == typeof(global::BEngine.BObject[]) ? (global::BEngine.BObject[])(object)querySource10321 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.BObject>)(object)querySource10321);
                foreach (var item in queryValues10321)
                {
                    if (ReferenceEquals(item, activeObject))
                        return true;
                }
            }
            else
            {
                foreach (var item in querySource10321)
                {
                    if (ReferenceEquals(item, activeObject))
                        return true;
                }
            }
        
            return false;
        }
        _activeContext = activeContext;
        NotifyHostSelectionChanged(activeObject, force: true);
    }
}
