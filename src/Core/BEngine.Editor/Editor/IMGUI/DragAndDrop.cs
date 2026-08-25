namespace BEngine.Editor;

public enum DragAndDropVisualMode
{
    None,
    Copy,
    Link,
    Move,
    Generic,
    Rejected
}

public static class DragAndDrop
{
    private static readonly Dictionary<string, object?> GenericData = new(StringComparer.Ordinal);
    private static BObject[] _objectReferences = [];
    private static string[] _paths = [];
    private static bool _accepted;

    public static BObject[] objectReferences
    {
        get => [.. _objectReferences];
        set => _objectReferences = value is null ? [] : [.. value.Where(static item => item is not null)];
    }

    public static string[] paths
    {
        get => [.. _paths];
        set => _paths = value is null
            ? []
            : [.. value.Where(static path => !string.IsNullOrWhiteSpace(path))];
    }

    public static DragAndDropVisualMode visualMode { get; set; }
    public static string title { get; private set; } = string.Empty;
    internal static bool isDragging { get; private set; }

    public static void PrepareStartDrag()
    {
        Clear();
        GUIUtility.hotControl = 0;
    }

    public static void StartDrag(string title)
    {
        DragAndDrop.title = title ?? string.Empty;
        isDragging = true;
        _accepted = false;
        visualMode = DragAndDropVisualMode.None;
    }

    public static void AcceptDrag() => _accepted = true;

    public static void SetGenericData(string key, object? data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (data is null) GenericData.Remove(key);
        else GenericData[key] = data;
    }

    public static object? GetGenericData(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return GenericData.GetValueOrDefault(key);
    }

    internal static object CaptureState() => new DragState(
        [.. _objectReferences],
        [.. _paths],
        new Dictionary<string, object?>(GenericData, StringComparer.Ordinal),
        visualMode,
        title,
        isDragging,
        _accepted);

    internal static void RestoreState(object state)
    {
        if (state is not DragState snapshot) return;
        _objectReferences = [.. snapshot.ObjectReferences];
        _paths = [.. snapshot.Paths];
        GenericData.Clear();
        foreach (var pair in snapshot.GenericData) GenericData[pair.Key] = pair.Value;
        visualMode = snapshot.VisualMode;
        title = snapshot.Title;
        isDragging = snapshot.IsDragging;
        _accepted = snapshot.Accepted;
    }

    internal static void BeginEvent(Event inputEvent)
    {
        if (!isDragging) return;
        _accepted = false;
        visualMode = DragAndDropVisualMode.None;
        if (inputEvent.type is EventType.MouseDrag or EventType.MouseMove)
            inputEvent.type = EventType.DragUpdated;
        else if (inputEvent.type == EventType.MouseUp)
            inputEvent.type = EventType.DragPerform;
        else if (inputEvent.type == EventType.MouseLeaveWindow)
            inputEvent.type = EventType.DragExited;
        else if (inputEvent.type == EventType.KeyDown && inputEvent.keyCode == KeyCode.Escape)
            Cancel();
    }

    internal static void EndEvent(Event inputEvent)
    {
        if (!isDragging) return;
        if (inputEvent.rawType is EventType.MouseUp or EventType.MouseLeaveWindow ||
            inputEvent.type is EventType.DragPerform or EventType.DragExited || _accepted)
        {
            Clear();
            GUIUtility.hotControl = 0;
        }
    }

    internal static void Cancel()
    {
        Clear();
        GUIUtility.hotControl = 0;
    }

    private static void Clear()
    {
        _objectReferences = [];
        _paths = [];
        GenericData.Clear();
        title = string.Empty;
        visualMode = DragAndDropVisualMode.None;
        isDragging = false;
        _accepted = false;
    }

    private sealed record DragState(
        BObject[] ObjectReferences,
        string[] Paths,
        IReadOnlyDictionary<string, object?> GenericData,
        DragAndDropVisualMode VisualMode,
        string Title,
        bool IsDragging,
        bool Accepted);
}
