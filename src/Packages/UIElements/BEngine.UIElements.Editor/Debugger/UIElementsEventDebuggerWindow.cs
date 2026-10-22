using BEngine.Editor;

namespace BEngine.UIElements.Editor;

[EditorWindowIcon("Icons/Windows/Console.png")]
public sealed class UIElementsEventDebuggerWindow :
    EditorWindow<UIElementsEventDebuggerWindowModel, UIElementsEventDebuggerWindowController>
{
    private static readonly string[] BuiltInEventTypes =
    [
        nameof(PointerDownEvent), nameof(ClickEvent), "ChangeEvent<T>"
    ];

    public UIElementsEventDebuggerWindow()
    {
        titleContent = new GUIContent("UIElements Event Debugger", "Icons/Windows/Console.png");
        minSize = new Vector2(760, 540);
        position = new Rect(100, 80, 1020, 720);
    }

    protected override UIElementsEventDebuggerWindowModel CreateWindowModel(
        EditorWindowMvcContext context) => new(context.WindowId);

    protected override UIElementsEventDebuggerWindowController CreateWindowController(
        EditorWindowMvcContext context,
        UIElementsEventDebuggerWindowModel windowModel) => new(context, windowModel);

    [MenuItem("Window/UIElement/Event Debugger", false, 231)]
    public static void Open() =>
        GetWindow<UIElementsEventDebuggerWindow>("UIElements Event Debugger", true).ShowAuxWindow();

    protected override void OnEnable() => controller.StartRecording();
    protected override void OnDisable() => controller.StopRecording();

    protected override void OnGUI()
    {
        var panels = BuildPanels();
        DrawToolbar(panels);
        GUILayout.Label("Events Log", EditorStyles.boldLabel);
        var visible = VisibleEvents();
        GUILayout.BeginHorizontal(EditorStyles.contentToolbar, GUILayout.Height(22));
        GUILayout.Label($"{visible.Count} event{(visible.Count == 1 ? string.Empty : "s")} " +
                        $"({(model.SelectedRecord is null ? 0 : 1)} selected)", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        GUILayout.Label(model.Suspended ? "Suspended" : "Recording", EditorStyles.miniBoldLabel);
        GUILayout.EndHorizontal();

        var eventHeight = Fix64.Max(130, position.height * (Fix64)0.28);
        DrawEventTable(GUILayoutUtility.GetRect(400, 4096, eventHeight, eventHeight,
            GUILayout.ExpandWidth(true)) , visible);
        DrawTransport();

        GUILayout.Label("Event Callbacks", EditorStyles.boldLabel);
        var callbackHeight = Fix64.Max(90, position.height * (Fix64)0.18);
        DrawCallbackTable(GUILayoutUtility.GetRect(400, 4096, callbackHeight, callbackHeight,
            GUILayout.ExpandWidth(true)), model.SelectedRecord);
        DrawDetails(model.SelectedRecord);
    }

    private void DrawToolbar(IReadOnlyList<PanelOption> panels)
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(24));
        var panelIndex = 0;
        for (var index = 1; index < panels.Count; index++)
        {
            if (!ReferenceEquals(panels[index].Root, model.SelectedPanelRoot)) continue;
            panelIndex = index;
            break;
        }
        var nextPanel = EditorGUILayout.Popup(string.Empty, panelIndex,
            panels.Select(static panel => panel.Name).ToArray(), EditorStyles.toolbarPopup,
            GUILayout.Width(220));
        if (nextPanel >= 0 && nextPanel < panels.Count && nextPanel != panelIndex)
            controller.SelectPanel(panels[nextPanel].Root);

        var eventTypes = KnownEventTypes();
        var enabledCount = eventTypes.Count(type => model.IsEventTypeEnabled(type));
        if (GUILayout.Button($"{enabledCount} selected event types", EditorStyles.toolbarDropDown,
                GUILayout.Width(170)))
            ShowEventTypeMenu(eventTypes, GUILayoutUtility.GetLastRect());
        model.Search = EditorToolbar.SearchField(model.Search, "Filter events",
            GUILayout.ExpandWidth(true));
        if (EditorToolbar.IconButton(model.Suspended ? EditorBuiltinIcons.Toolbar.Play :
                EditorBuiltinIcons.Toolbar.Pause, model.Suspended ? "Resume event capture" :
                "Suspend event capture")) controller.ToggleSuspended();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Clear, "Clear event log"))
            controller.Clear();
        GUILayout.EndHorizontal();
    }

    private void DrawTransport()
    {
        GUILayout.BeginHorizontal(EditorStyles.contentToolbar, GUILayout.Height(23));
        if (EditorToolbar.IconButton(model.Suspended ? EditorBuiltinIcons.Toolbar.Play :
                EditorBuiltinIcons.Toolbar.Pause, model.Suspended ? "Resume" : "Suspend"))
            controller.ToggleSuspended();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Stop, "Clear log")) controller.Clear();
        GUILayout.FlexibleSpace();
        GUILayout.Label("Max Log", EditorStyles.miniLabel, GUILayout.Width(48));
        var maximum = EditorGUILayout.IntField(string.Empty, model.MaxLogEntries, EditorStyles.miniTextField,
            GUILayout.Width(72));
        if (maximum != model.MaxLogEntries) controller.SetMaxLogEntries(maximum);
        GUILayout.EndHorizontal();
    }

    private void DrawEventTable(Rect rect, IReadOnlyList<UIEventDebugRecord> events)
    {
        GUI.Box(rect, string.Empty, EditorStyles.viewBackground);
        var width = rect.width;
        var header = new Rect(rect.x, rect.y, width, 22);
        GUI.Box(header, string.Empty, EditorStyles.contentToolbar);
        GUI.Label(new Rect(header.x + 4, header.y, 92, header.height), "Timestamp", EditorStyles.miniBoldLabel);
        GUI.Label(new Rect(header.x + 100, header.y, width * (Fix64)0.31, header.height), "Event",
            EditorStyles.miniBoldLabel);
        GUI.Label(new Rect(header.x + width * (Fix64)0.47, header.y, width * (Fix64)0.39, header.height),
            "Target", EditorStyles.miniBoldLabel);
        GUI.Label(new Rect(header.x + width - 76, header.y, 72, header.height), "Stopped",
            EditorStyles.miniBoldLabel);

        var viewport = new Rect(rect.x, rect.y + 22, width, Fix64.Max(1, rect.height - 22));
        var contentWidth = Fix64.Max(1, viewport.width - 14);
        var contentHeight = Fix64.Max(viewport.height, events.Count * 21);
        model.EventScroll = GUI.BeginScrollView(viewport, model.EventScroll,
            new Rect(0, 0, contentWidth, contentHeight));
        try
        {
            for (var index = 0; index < events.Count; index++)
            {
                var record = events[index];
                var row = new Rect(0, index * 21, contentWidth, 21);
                var selected = model.SelectedDispatchId == record.DispatchId;
                if (GUI.Button(row, GUIContent.none,
                        selected ? EditorStyles.treeViewRowSelected : EditorStyles.treeViewRow))
                    controller.Select(record.DispatchId);
                GUI.Label(new Rect(4, row.y, 92, row.height), FormatTimestamp(record.Timestamp),
                    EditorStyles.miniLabel);
                GUI.Label(new Rect(100, row.y, contentWidth * (Fix64)0.31, row.height), record.EventType,
                    EditorStyles.miniLabel);
                GUI.Label(new Rect(contentWidth * (Fix64)0.47, row.y, contentWidth * (Fix64)0.39, row.height),
                    record.TargetPath, EditorStyles.miniLabel);
                GUI.Label(new Rect(contentWidth - 76, row.y, 72, row.height),
                    record.PropagationStopped ? "Yes" : string.Empty, EditorStyles.centeredMiniLabel);
            }
        }
        finally { GUI.EndScrollView(); }
    }

    private void DrawCallbackTable(Rect rect, UIEventDebugRecord? record)
    {
        var callbacks = record?.Callbacks ?? [];
        GUI.Box(rect, string.Empty, EditorStyles.viewBackground);
        var header = new Rect(rect.x, rect.y, rect.width, 22);
        GUI.Box(header, string.Empty, EditorStyles.contentToolbar);
        GUI.Label(new Rect(header.x + 4, header.y, 92, header.height), "Timestamp", EditorStyles.miniBoldLabel);
        GUI.Label(new Rect(header.x + 100, header.y, header.width * (Fix64)0.43, header.height), "Handler",
            EditorStyles.miniBoldLabel);
        GUI.Label(new Rect(header.x + header.width * (Fix64)0.58, header.y, 100, header.height), "Phase",
            EditorStyles.miniBoldLabel);
        GUI.Label(new Rect(header.x + header.width * (Fix64)0.73, header.y,
            header.width * (Fix64)0.26, header.height), "Current Target", EditorStyles.miniBoldLabel);

        var viewport = new Rect(rect.x, rect.y + 22, rect.width, Fix64.Max(1, rect.height - 22));
        var contentWidth = Fix64.Max(1, viewport.width - 14);
        var contentHeight = Fix64.Max(viewport.height, callbacks.Count * 21);
        model.CallbackScroll = GUI.BeginScrollView(viewport, model.CallbackScroll,
            new Rect(0, 0, contentWidth, contentHeight));
        try
        {
            for (var index = 0; index < callbacks.Count; index++)
            {
                var callback = callbacks[index];
                var y = index * 21;
                GUI.Label(new Rect(4, y, 92, 21), FormatTimestamp(callback.Timestamp),
                    EditorStyles.miniLabel);
                GUI.Label(new Rect(100, y, contentWidth * (Fix64)0.43, 21), callback.Handler,
                    EditorStyles.miniLabel);
                GUI.Label(new Rect(contentWidth * (Fix64)0.58, y, 100, 21),
                    callback.PropagationPhase.ToString(), EditorStyles.miniLabel);
                GUI.Label(new Rect(contentWidth * (Fix64)0.73, y, contentWidth * (Fix64)0.26, 21),
                    callback.CurrentTarget, EditorStyles.miniLabel);
            }
        }
        finally { GUI.EndScrollView(); }
    }

    private void DrawDetails(UIEventDebugRecord? record)
    {
        GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
        var half = Fix64.Max(200, (position.width - 8) / 2);
        model.PathScroll = DrawTextPanel(
            "Event Propagation Paths", record?.PropagationPath ?? [], model.PathScroll, half);
        model.InformationScroll = DrawTextPanel(
            "Event Information", record?.Information ?? [], model.InformationScroll, half);
        GUILayout.EndHorizontal();
    }

    private static Vector2 DrawTextPanel(
        string title,
        IReadOnlyList<string> lines,
        Vector2 scroll,
        Fix64 width)
    {
        GUILayout.BeginVertical(GUILayout.Width(width), GUILayout.ExpandHeight(true));
        GUILayout.Label(title, EditorStyles.boldLabel);
        var rect = GUILayoutUtility.GetRect(180, 4096, 70, 4096,
            GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        GUI.Box(rect, string.Empty, EditorStyles.viewBackground);
        var contentHeight = Fix64.Max(rect.height, lines.Count * 20 + 4);
        scroll = GUI.BeginScrollView(rect, scroll,
            new Rect(0, 0, Fix64.Max(1, rect.width - 14), contentHeight));
        try
        {
            for (var index = 0; index < lines.Count; index++)
                GUI.Label(new Rect(4, 2 + index * 20, Fix64.Max(1, rect.width - 22), 20),
                    lines[index], EditorStyles.miniLabel);
        }
        finally { GUI.EndScrollView(); }
        GUILayout.EndVertical();
        return scroll;
    }

    private IReadOnlyList<UIEventDebugRecord> VisibleEvents()
    {
        IEnumerable<UIEventDebugRecord> result = model.Events;
        if (model.SelectedPanelRoot is { } panel)
            result = result.Where(record => ReferenceEquals(record.PanelRoot, panel));
        result = result.Where(record => model.IsEventTypeEnabled(NormalizeEventType(record.EventType)));
        if (!string.IsNullOrWhiteSpace(model.Search))
            result = result.Where(record => record.EventType.Contains(model.Search,
                                                 StringComparison.OrdinalIgnoreCase) ||
                                             record.TargetPath.Contains(model.Search,
                                                 StringComparison.OrdinalIgnoreCase));
        return result.Reverse().ToArray();
    }

    private IReadOnlyList<PanelOption> BuildPanels()
    {
        var panels = new List<PanelOption> { new("All panels", null) };
        foreach (var document in BObject.FindObjectsByType<UIDocument>())
        {
            VisualElement root;
            string name;
            try
            {
                root = document.rootVisualElement;
                name = $"{document.gameObject.name} (UIDocument)";
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            if (panels.All(panel => !ReferenceEquals(panel.Root, root))) panels.Add(new PanelOption(name, root));
        }
        foreach (var record in model.Events)
        {
            if (record.PanelRoot is not { } root || panels.Any(panel => ReferenceEquals(panel.Root, root))) continue;
            panels.Add(new PanelOption($"{UIElementsEventDebuggerWindowController.ElementName(root)} (detached)", root));
        }
        return panels;
    }

    private IReadOnlyList<string> KnownEventTypes() => BuiltInEventTypes
        .Concat(model.Events.Select(record => NormalizeEventType(record.EventType)))
        .Distinct(StringComparer.Ordinal).OrderBy(static name => name, StringComparer.Ordinal).ToArray();

    private void ShowEventTypeMenu(IEnumerable<string> eventTypes, Rect position)
    {
        var menu = new GenericMenu();
        foreach (var eventType in eventTypes)
        {
            var captured = eventType;
            menu.AddItem(new GUIContent(eventType), model.IsEventTypeEnabled(eventType),
                () => controller.ToggleEventType(captured));
        }
        menu.DropDown(position);
    }

    private static string NormalizeEventType(string eventType) =>
        eventType.StartsWith("ChangeEvent`", StringComparison.Ordinal) ||
        eventType.StartsWith("ChangeEvent<", StringComparison.Ordinal)
            ? "ChangeEvent<T>"
            : eventType;

    private static string FormatTimestamp(Fix64 timestamp) =>
        TimeSpan.FromSeconds((double)timestamp).ToString(@"hh\:mm\:ss\.fff");

    private sealed record PanelOption(string Name, VisualElement? Root);
}
