
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
        get => EditorBridge.Host?.ActiveObject is { isDestroyed: false } active
            ? active : objects.FirstOrDefault();
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
        get => activeObject switch { GameObject go => go, Component component => component.GameObjectOrNull, _ => null };
        set => activeObject = value;
    }

    public static Transform? activeTransform => activeGameObject?.transform;
    public static BObject? activeContext { get => _activeContext; set => _activeContext = value; }
    public static BObject[] objects
    {
        get => _objects.Where(item => !item.isDestroyed).ToArray();
        set
        {
            _objects = value?.Where(item => item is { isDestroyed: false })
                .DistinctBy(item => item.GetInstanceID()).ToArray() ?? [];
            var first = _objects.FirstOrDefault();
            if (EditorBridge.Host is { } host) host.ActiveObject = first;
            NotifyHostSelectionChanged(first);
        }
    }
    public static GameObject[] gameObjects => objects.Select(item => item switch
        { GameObject gameObject => gameObject, Component component => component.GameObjectOrNull, _ => null })
        .Where(item => item is { isDestroyed: false }).Cast<GameObject>().DistinctBy(item => item.Id).ToArray();
    public static Transform[] transforms => gameObjects.Select(item => item.transform).ToArray();
    public static int count => objects.Length;
    public static int activeInstanceID => activeObject?.GetInstanceID() ?? 0;
    public static int[] instanceIDs
    {
        get => objects.Select(item => item.GetInstanceID()).ToArray();
        set => objects = value?.Select(BObject.FindObjectFromInstanceID)
            .OfType<BObject>().ToArray() ?? [];
    }

    public static bool Contains(BObject? target) => target is not null &&
        objects.Any(item => ReferenceEquals(item, target));
    public static bool Contains(int instanceId) => objects.Any(item => item.GetInstanceID() == instanceId);
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
        foreach (var root in roots)
        {
            if (mode.HasFlag(SelectionMode.TopLevel) && HasSelectedAncestor(root, selected)) continue;
            var pending = new Stack<GameObject>();
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
        Component component => component.GameObjectOrNull,
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
        _objects = objects;
        if (selected is not null && !_objects.Any(item => ReferenceEquals(item, selected))) _objects = [selected];
        if (selected is null) _objects = [];
        var id = selected?.Id;
        var selectedIds = _objects.Select(item => item.GetInstanceID()).ToArray();
        if (!force && _lastSelection == id && _lastObjects.SequenceEqual(selectedIds)) return;
        _lastSelection = id;
        _lastObjects = selectedIds;
        EditorCallbackDispatcher.Invoke(selectionChanged, nameof(selectionChanged));
        EditorWindow.NotifySelectionChanged();
    }

    internal static void RestoreHostSelection(
        IEnumerable<BObject> objects,
        BObject? activeContext,
        BObject? activeObject)
    {
        ArgumentNullException.ThrowIfNull(objects);
        _objects = objects.Where(item => item is { isDestroyed: false }).DistinctBy(item => item.GetInstanceID()).ToArray();
        if (activeObject is { isDestroyed: false } && !_objects.Any(item => ReferenceEquals(item, activeObject)))
            _objects = [activeObject, .. _objects];
        _activeContext = activeContext;
        NotifyHostSelectionChanged(activeObject, force: true);
    }
}
