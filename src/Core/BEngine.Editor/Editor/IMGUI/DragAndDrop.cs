namespace BEngine.Editor;

public static class DragAndDrop
{
    public delegate DragAndDropVisualMode ProjectBrowserDropHandler(
        int dragInstanceId, string dropUponPath, bool perform);

    public delegate DragAndDropVisualMode SceneDropHandler(
        BObject? dropUpon, Vector2 worldPosition, Vector2 viewportPosition,
        Transform? parentForDraggedObjects, bool perform);

    public delegate DragAndDropVisualMode InspectorDropHandler(BObject[] targets, bool perform);

    public delegate DragAndDropVisualMode HierarchyDropHandler(
        int dropTargetInstanceId, HierarchyDropFlags dropMode,
        Transform? parentForDraggedObjects, bool perform);

    private static readonly Fix64 StartDragDistance = (Fix64)6;
    private static readonly Dictionary<string, object?> GenericData = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, List<Delegate>> DropHandlers = [];
    private static readonly Dictionary<int, Vector2> DelayedDragOrigins = [];
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

    public static int activeControlID { get; set; }
    public static DragAndDropVisualMode visualMode { get; set; }
    public static string title { get; private set; } = string.Empty;
    internal static bool isDragging { get; private set; }
    internal static bool accepted => _accepted;

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
        visualMode = DragAndDropVisualMode.Rejected;
    }

    public static void AcceptDrag() => _accepted = true;

    public static void SetGenericData(string type, object? data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        GenericData[type] = data;
    }

    public static object? GetGenericData(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return GenericData.GetValueOrDefault(type);
    }

    public static bool HasHandler(int dropDestinationId, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return DropHandlers.TryGetValue(dropDestinationId, out var handlers) && handlers.Contains(handler);
    }

    public static void AddDropHandler(ProjectBrowserDropHandler handler) =>
        AddDropHandler(DragAndDropWindowTarget.ProjectBrowser, handler);

    public static void AddDropHandler(SceneDropHandler handler) =>
        AddDropHandler(DragAndDropWindowTarget.SceneView, handler);

    public static void AddDropHandler(InspectorDropHandler handler) =>
        AddDropHandler(DragAndDropWindowTarget.Inspector, handler);

    public static void AddDropHandler(HierarchyDropHandler handler) =>
        AddDropHandler(DragAndDropWindowTarget.Hierarchy, handler);

    public static void RemoveDropHandler(ProjectBrowserDropHandler handler) =>
        RemoveDropHandler(DragAndDropWindowTarget.ProjectBrowser, handler);

    public static void RemoveDropHandler(SceneDropHandler handler) =>
        RemoveDropHandler(DragAndDropWindowTarget.SceneView, handler);

    public static void RemoveDropHandler(InspectorDropHandler handler) =>
        RemoveDropHandler(DragAndDropWindowTarget.Inspector, handler);

    public static void RemoveDropHandler(HierarchyDropHandler handler) =>
        RemoveDropHandler(DragAndDropWindowTarget.Hierarchy, handler);

    internal static DragAndDropVisualMode DropOnProjectBrowserWindow(
        int dragUponInstanceId, string dropUponPath, bool perform) =>
        Drop(DragAndDropWindowTarget.ProjectBrowser, dragUponInstanceId, dropUponPath ?? string.Empty, perform);

    internal static DragAndDropVisualMode DropOnSceneWindow(
        BObject? dropUpon, Vector2 worldPosition, Vector2 viewportPosition,
        Transform? parentForDraggedObjects, bool perform) =>
        Drop(DragAndDropWindowTarget.SceneView, dropUpon, worldPosition, viewportPosition,
            parentForDraggedObjects, perform);

    internal static DragAndDropVisualMode DropOnInspectorWindow(BObject[] targets, bool perform)
    {
        BObject[] safeTargets = targets is null ? [] : [.. targets];
        return Drop(DragAndDropWindowTarget.Inspector, safeTargets, perform);
    }

    internal static DragAndDropVisualMode DropOnHierarchyWindow(
        int dropTargetInstanceId, HierarchyDropFlags dropMode,
        Transform? parentForDraggedObjects, bool perform) =>
        Drop(DragAndDropWindowTarget.Hierarchy, dropTargetInstanceId, dropMode,
            parentForDraggedObjects, perform);

    internal static bool HandleDelayedDrag(Rect position, int id, BObject objectToDrag)
    {
        ArgumentNullException.ThrowIfNull(objectToDrag);
        var current = Event.current;
        switch (current.GetTypeForControl(id))
        {
            case EventType.MouseDown when current.button == 0 && current.clickCount <= 1 &&
                                              position.Contains(current.mousePosition):
                GUIUtility.hotControl = id;
                DelayedDragOrigins[id] = current.mousePosition;
                return true;
            case EventType.MouseDrag when GUIUtility.hotControl == id &&
                                          DelayedDragOrigins.TryGetValue(id, out var origin):
                var delta = current.mousePosition - origin;
                if (delta.x * delta.x + delta.y * delta.y < StartDragDistance * StartDragDistance)
                    return false;
                GUIUtility.hotControl = 0;
                PrepareStartDrag();
                activeControlID = id;
                objectReferences = [objectToDrag];
                StartDrag(objectToDrag.ToString());
                DelayedDragOrigins.Remove(id);
                return true;
            case EventType.MouseUp when GUIUtility.hotControl == id:
                GUIUtility.hotControl = 0;
                DelayedDragOrigins.Remove(id);
                break;
        }
        return false;
    }

    internal static object CaptureState() => new DragState(
        [.. _objectReferences],
        [.. _paths],
        new Dictionary<string, object?>(GenericData, StringComparer.Ordinal),
        visualMode,
        title,
        activeControlID,
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
        activeControlID = snapshot.ActiveControlId;
        isDragging = snapshot.IsDragging;
        _accepted = snapshot.Accepted;
    }

    internal static void BeginEvent(Event inputEvent)
    {
        if (!isDragging) return;
        if (inputEvent.type == EventType.MouseDown)
        {
            Cancel();
            return;
        }
        if (inputEvent.type is EventType.MouseDrag or EventType.MouseMove)
        {
            _accepted = false;
            activeControlID = 0;
            visualMode = DragAndDropVisualMode.Rejected;
            inputEvent.type = EventType.DragUpdated;
        }
        else if (inputEvent.type == EventType.MouseUp)
        {
            _accepted = false;
            inputEvent.type = EventType.DragPerform;
        }
        else if (inputEvent.type == EventType.KeyDown && inputEvent.keyCode == KeyCode.Escape)
            Cancel();
    }

    internal static void ApplyCursor()
    {
        if (!isDragging) return;
        var cursor = visualMode switch
        {
            DragAndDropVisualMode.Copy => MouseCursor.ArrowPlus,
            DragAndDropVisualMode.Link => MouseCursor.Link,
            DragAndDropVisualMode.Move => MouseCursor.MoveArrow,
            DragAndDropVisualMode.Rejected => MouseCursor.ArrowMinus,
            _ => MouseCursor.ArrowMinus
        };
        GUI.AddCursorRect(new Rect(0, 0, GUIUtility.currentViewWidth, GUIUtility.currentViewHeight), cursor);
    }

    internal static void EndEvent(Event inputEvent)
    {
        if (!isDragging && !_accepted && inputEvent.type != EventType.DragPerform &&
            inputEvent.rawType is not (EventType.MouseUp or EventType.DragPerform or EventType.DragExited)) return;
        // Losing focus is expected while crossing between main and native floating editor windows.
        // Keep the global payload alive until the button is released, accepted, cancelled, or a new
        // click starts. This mirrors Unity's editor-wide DragAndDrop session.
        if (inputEvent.rawType is EventType.MouseUp or EventType.DragPerform or EventType.DragExited ||
            inputEvent.type == EventType.DragPerform || _accepted)
        {
            Clear();
            GUIUtility.hotControl = 0;
        }
    }

    internal static void Cleanup() => Cancel();

    internal static void Cancel()
    {
        Clear();
        GUIUtility.hotControl = 0;
    }

    internal static bool HasGenericDragData() => GenericData.Count > 0;

    internal static void ClearDropHandlers() => DropHandlers.Clear();

    private static void AddDropHandler(int dropDestinationId, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (HasHandler(dropDestinationId, handler))
            throw new InvalidOperationException(
                $"The drop handler is already registered for destination {dropDestinationId}.");
        if (!DropHandlers.TryGetValue(dropDestinationId, out var handlers))
            DropHandlers[dropDestinationId] = handlers = [];
        handlers.Add(handler);
    }

    private static void RemoveDropHandler(int dropDestinationId, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!DropHandlers.TryGetValue(dropDestinationId, out var handlers)) return;
        handlers.RemoveAll(candidate => candidate == handler);
        if (handlers.Count == 0) DropHandlers.Remove(dropDestinationId);
    }

    private static DragAndDropVisualMode Drop(int dropDestinationId, params object?[] args)
    {
        if (!DropHandlers.TryGetValue(dropDestinationId, out var handlers))
            return DragAndDropVisualMode.Rejected;
        var snapshot = handlers.ToArray();
        for (var index = snapshot.Length - 1; index >= 0; index--)
        {
            var mode = Invoke(snapshot[index], args);
            if (mode != DragAndDropVisualMode.None) return mode;
        }
        return DragAndDropVisualMode.None;
    }

    private static DragAndDropVisualMode Invoke(Delegate handler, object?[] args) => handler switch
    {
        ProjectBrowserDropHandler project => project((int)args[0]!, (string)args[1]!, (bool)args[2]!),
        SceneDropHandler scene => scene((BObject?)args[0], (Vector2)args[1]!, (Vector2)args[2]!,
            (Transform?)args[3], (bool)args[4]!),
        InspectorDropHandler inspector => inspector((BObject[])args[0]!, (bool)args[1]!),
        HierarchyDropHandler hierarchy => hierarchy((int)args[0]!, (HierarchyDropFlags)args[1]!,
            (Transform?)args[2], (bool)args[3]!),
        _ => throw new ArgumentException($"Unsupported drop handler type {handler.GetType().FullName}.",
            nameof(handler))
    };

    private static void Clear()
    {
        _objectReferences = [];
        _paths = [];
        GenericData.Clear();
        DelayedDragOrigins.Clear();
        title = string.Empty;
        activeControlID = 0;
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
        int ActiveControlId,
        bool IsDragging,
        bool Accepted);
}
