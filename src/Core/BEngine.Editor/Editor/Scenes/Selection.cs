
namespace BEngine.Editor;

public static class Selection
{
    private static Guid? _lastSelection;
    private static BObject[] _objects = [];
    private static BObject? _activeContext;

    public static event Action? selectionChanged;

    public static BObject? activeObject
    {
        get => EditorBridge.Host?.ActiveObject ?? _objects.FirstOrDefault();
        set
        {
            _objects = value is null ? [] : [value];
            if (EditorBridge.Host is { } host)
            {
                host.ActiveObject = value;
                NotifyHostSelectionChanged(value);
            }
        }
    }

    public static GameObject? activeGameObject
    {
        get => EditorBridge.Host?.ActiveGameObject;
        set
        {
            if (EditorBridge.Host is { } host)
            {
                host.ActiveGameObject = value;
                NotifyHostSelectionChanged(value);
            }
        }
    }

    public static Transform? activeTransform => activeGameObject?.transform;
    public static BObject? activeContext { get => _activeContext; set => _activeContext = value; }
    public static BObject[] objects
    {
        get => [.. _objects];
        set
        {
            _objects = value?.Where(item => item is not null).DistinctBy(item => item.Id).ToArray() ?? [];
            var first = _objects.FirstOrDefault();
            if (EditorBridge.Host is { } host) host.ActiveObject = first;
            NotifyHostSelectionChanged(first);
        }
    }
    public static GameObject[] gameObjects => objects.Select(item => item switch
        { GameObject gameObject => gameObject, Component component => component.gameObject, _ => null })
        .Where(item => item is not null).Cast<GameObject>().DistinctBy(item => item.Id).ToArray();
    public static Transform[] transforms => gameObjects.Select(item => item.transform).ToArray();
    public static int count => _objects.Length;
    public static int activeInstanceID => activeObject?.GetInstanceID() ?? 0;
    public static int[] instanceIDs
    {
        get => objects.Select(item => item.GetInstanceID()).ToArray();
        set => activeObject = value?.Select(BObject.FindObjectFromInstanceID).FirstOrDefault(item => item is not null);
    }

    public static bool Contains(BObject? target) => target is not null && ReferenceEquals(activeObject, target);
    public static bool Contains(int instanceId) => activeObject?.GetInstanceID() == instanceId;
    public static T[] GetFiltered<T>(SelectionMode mode = SelectionMode.Unfiltered) where T : BObject =>
        objects.OfType<T>().ToArray();

    internal static void NotifyHostSelectionChanged(BObject? selected)
    {
        if (selected is not null && !_objects.Any(item => ReferenceEquals(item, selected))) _objects = [selected];
        if (selected is null) _objects = [];
        var id = selected?.Id;
        if (_lastSelection == id) return;
        _lastSelection = id;
        EditorCallbackDispatcher.Invoke(selectionChanged, nameof(selectionChanged));
        EditorWindow.NotifySelectionChanged();
    }
}
