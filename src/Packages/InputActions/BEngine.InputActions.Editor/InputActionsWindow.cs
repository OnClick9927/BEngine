using BEngine.Editor;

namespace BEngine.InputActions.Editor;

[EditorWindowIcon("Icons/Assets/AssetScript.png")]
public sealed class InputActionsWindow : EditorWindow<InputActionsWindowModel, InputActionsWindowController>
{
    private readonly EditorScrollView _maps = new(), _actions = new(), _details = new();
    private Vector2 _mapScroll, _actionScroll, _detailScroll;
    public InputActionsWindow() { titleContent = new("Input Actions"); minSize = new(760, 420); }
    protected override InputActionsWindowModel CreateWindowModel(EditorWindowMvcContext context) => new(context.WindowId);
    protected override InputActionsWindowController CreateWindowController(EditorWindowMvcContext context, InputActionsWindowModel model) => new(context, model);
    [MenuItem("Window/Input/Input Actions", false, 238)]
    public static void Open() => GetWindow<InputActionsWindow>("Input Actions").Show();
    public static void Open(InputActionAsset asset)
    { var window = GetWindow<InputActionsWindow>("Input Actions"); window.controller.Select(asset); window.Show(); }
    [OnOpenAsset]
    private static bool OpenAsset(string path)
    {
        if (AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(InputActionAsset) || AssetDatabase.LoadAssetAtPath<InputActionAsset>(path) is not { } asset) return false;
        Open(asset); return true;
    }
    protected override void OnSelectionChange() { if (Selection.activeObject is InputActionAsset asset && !ReferenceEquals(model.Asset, asset)) controller.Select(asset); }
    protected override void OnEnable() { OnSelectionChange(); Undo.undoRedoPerformed += OnUndo; EditorApplication.playModeStateChanged += OnPlay; }
    protected override void OnDisable() { controller.StopPreview(); Undo.undoRedoPerformed -= OnUndo; EditorApplication.playModeStateChanged -= OnPlay; }
    protected override void OnLostFocus() => controller.StopPreview();
    private void OnUndo() { controller.StopPreview(); Repaint(); }
    private void OnPlay(PlayModeStateChange _) { controller.StopPreview(); Repaint(); }
    protected override void Update() => controller.TickPreview();

    protected override void OnGUI()
    {
        controller.HandleInputEvent(Event.current);
        var width = GUIUtility.currentViewWidth; var height = GUIUtility.currentViewHeight;
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
        {
            if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(45))) controller.Select(new InputActionAsset { name = "Input Actions" });
            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(45))) controller.Save();
        }
        var selected = EditorGUILayout.ObjectField("", model.Asset, false, GUILayout.Width(180));
        if (!ReferenceEquals(selected, model.Asset)) controller.Select(selected);
        model.Search = EditorGUILayout.TextField("Search", model.Search, GUILayout.Width(200));
        GUILayout.EndHorizontal();
        if (model.Asset is not { isDestroyed: false } asset) { EditorGUILayout.HelpBox("Create or select an Input Actions asset."); return; }
        if (width < 700 || height < 300) { EditorGUILayout.HelpBox("Resize the window to edit maps, actions and bindings."); return; }
        var availableHeight = height - 72;
        using (GUILayout.Area(new Rect(5, 32, 170, availableHeight)))
        {
            _mapScroll = _maps.Begin(_mapScroll);
            try
            {
                GUILayout.Label("Action maps", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
                {
                    GUILayout.BeginHorizontal(); if (GUILayout.Button("+ Map")) controller.AddMap();
                    using (new EditorGUI.DisabledScope(model.Map is null)) if (GUILayout.Button("Delete")) controller.DeleteMap();
                    GUILayout.EndHorizontal();
                }
                for (var i = 0; i < asset.maps.Count; i++)
                    if (Matches(asset.maps[i].name) && GUILayout.Button((i == model.MapIndex ? "• " : "") + asset.maps[i].name)) controller.SelectMap(i);
            }
            finally { _maps.End(); }
        }
        using (GUILayout.Area(new Rect(185, 32, 200, availableHeight)))
        {
            _actionScroll = _actions.Begin(_actionScroll);
            try
            {
                GUILayout.Label("Actions", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite || model.Map is null))
                {
                    GUILayout.BeginHorizontal(); if (GUILayout.Button("+ Action")) controller.AddAction();
                    using (new EditorGUI.DisabledScope(model.Action is null)) if (GUILayout.Button("Delete")) controller.DeleteAction();
                    GUILayout.EndHorizontal();
                }
                if (model.Map is { } map)
                    for (var i = 0; i < map.actions.Count; i++)
                    {
                        var action = map.actions[i];
                        if (Matches(action.name) && GUILayout.Button((i == model.ActionIndex ? "• " : "") + action.name + "  [" + action.type + "]")) controller.SelectAction(i);
                    }
                DrawPreview();
            }
            finally { _actions.End(); }
        }
        using (GUILayout.Area(new Rect(395, 32, width - 400, availableHeight)))
        {
            _detailScroll = _details.Begin(_detailScroll);
            try { DrawDetails(asset); } finally { _details.End(); }
        }
        GUI.Label(new Rect(6, height - 34, width - 12, 30), model.Message, EditorStyles.wordWrappedLabel);
    }

    private void DrawDetails(InputActionAsset asset)
    {
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite))
        {
            if (model.Map is { } map)
            {
                var name = EditorGUILayout.TextField("Map name", map.name);
                if (name != map.name) controller.Edit("Rename input map", () => map.name = name);
            }
            if (model.Action is { } action)
            {
                EditorGUI.BeginChangeCheck();
                var name = EditorGUILayout.TextField("Action name", action.name);
                var type = (InputActionType)EditorGUILayout.EnumPopup("Type", action.type);
                var interaction = type == InputActionType.Button ? (InputInteraction)EditorGUILayout.EnumPopup("Interaction", action.interaction) : InputInteraction.Press;
                var hold = action.holdDuration; var tap = action.tapDuration; var interval = action.tapInterval; var count = action.tapCount;
                if (interaction == InputInteraction.Hold) hold = Number("Hold seconds", hold);
                if (interaction is InputInteraction.Tap or InputInteraction.MultiTap) tap = Number("Tap seconds", tap);
                if (interaction == InputInteraction.MultiTap) { interval = Number("Tap interval", interval); count = EditorGUILayout.IntField("Tap count", count); }
                if (EditorGUI.EndChangeCheck()) controller.Edit("Edit input action", () =>
                { action.name = name; action.type = type; action.interaction = interaction; action.holdDuration = hold; action.tapDuration = tap; action.tapInterval = interval; action.tapCount = count; });
                GUILayout.Space(8); GUILayout.Label("Bindings", EditorStyles.boldLabel);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("+ Binding")) controller.AddBinding();
                using (new EditorGUI.DisabledScope(model.Binding is null))
                { if (GUILayout.Button("Duplicate")) controller.DuplicateBinding(); if (GUILayout.Button("Delete")) controller.DeleteBinding(); }
                GUILayout.EndHorizontal();
                for (var i = 0; i < action.bindings.Count; i++)
                {
                    var binding = action.bindings[i]; var label = binding.control + (binding.scheme.Length == 0 ? "" : " • " + binding.scheme);
                    if (Matches(label) && GUILayout.Button((i == model.BindingIndex ? "• " : "") + label)) controller.SelectBinding(i);
                }
                if (model.Binding is { } current) DrawBinding(current);
            }
            GUILayout.Space(12); GUILayout.Label("Touch gesture recognition", EditorStyles.boldLabel);
            var settings = asset.touchGestures;
            EditorGUI.BeginChangeCheck();
            var duration = Number("Tap seconds", settings.tapDuration); var holdDuration = Number("Long press seconds", settings.longPressDuration);
            var tolerance = Number("Tap tolerance (px)", settings.tapDistance); var distance = Number("Swipe distance (px)", settings.swipeDistance);
            var maxDuration = Number("Swipe seconds", settings.swipeDuration);
            if (EditorGUI.EndChangeCheck()) controller.Edit("Edit touch gestures", () =>
            { settings.tapDuration = duration; settings.longPressDuration = holdDuration; settings.tapDistance = tolerance; settings.swipeDistance = distance; settings.swipeDuration = maxDuration; });
        }
    }
    private void DrawBinding(InputBinding binding)
    {
        GUILayout.Space(8); GUILayout.Label("ID: " + binding.id, EditorStyles.miniLabel);
        EditorGUI.BeginChangeCheck(); var copy = InputActionsWindowController.Copy(binding);
        copy.scheme = EditorGUILayout.TextField("Control scheme", copy.scheme);
        copy.control = (InputControlKind)EditorGUILayout.EnumPopup("Control", copy.control);
        if (copy.control is InputControlKind.Key or InputControlKind.KeyAxis or InputControlKind.KeyVector2)
            copy.key = (KeyCode)EditorGUILayout.EnumPopup(copy.control == InputControlKind.Key ? "Key / mouse button" : "Positive / right", copy.key);
        if (copy.control is InputControlKind.KeyAxis or InputControlKind.KeyVector2) copy.negative = (KeyCode)EditorGUILayout.EnumPopup("Negative / left", copy.negative);
        if (copy.control == InputControlKind.KeyVector2)
        { copy.up = (KeyCode)EditorGUILayout.EnumPopup("Up", copy.up); copy.down = (KeyCode)EditorGUILayout.EnumPopup("Down", copy.down); }
        if (copy.control == InputControlKind.GamepadButton) copy.button = (GamepadButton)EditorGUILayout.EnumPopup("Button", copy.button);
        if (copy.control is InputControlKind.GamepadAxis or InputControlKind.GamepadStick) copy.axis = (GamepadAxis)EditorGUILayout.EnumPopup("Axis X", copy.axis);
        if (copy.control == InputControlKind.GamepadStick) copy.axisY = (GamepadAxis)EditorGUILayout.EnumPopup("Axis Y", copy.axisY);
        if (copy.control is >= InputControlKind.TouchPress and <= InputControlKind.TouchSwipe)
            copy.fingerId = EditorGUILayout.IntField("Finger ID (-1 = primary/any)", copy.fingerId);
        if (copy.control == InputControlKind.TouchPress) copy.requiredTouches = EditorGUILayout.IntField("Minimum contacts", copy.requiredTouches);
        copy.deadZone = Number("Dead zone", copy.deadZone); copy.scale = Number("Scale", copy.scale);
        if (EditorGUI.EndChangeCheck()) controller.SetBinding(copy);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("↑")) controller.MoveBinding(-1);
        if (GUILayout.Button("↓")) controller.MoveBinding(1);
        if (GUILayout.Button("Capture button…")) controller.CaptureBinding();
        GUILayout.EndHorizontal();
        if (copy.control >= InputControlKind.TouchPosition)
            GUILayout.Label("Touch positions/deltas use pixels; rotation uses degrees. Pinch/pan use the same two finger IDs until that pair changes.", EditorStyles.wordWrappedLabel);
    }
    private void DrawPreview()
    {
        GUILayout.Space(12); GUILayout.Label("Input monitor", EditorStyles.boldLabel);
        var slots = Input.GetGamepads(); var ids = new[] { -1 }.Concat(slots.Select(slot => slot.index)).ToArray();
        var labels = new[] { "No gamepad" }.Concat(slots.Select(slot => $"{slot.index}: {slot.name}" + (slot.connected ? "" : " (disconnected)"))).ToArray();
        var index = EditorGUILayout.Popup("Gamepad", Math.Max(0, Array.IndexOf(ids, model.GamepadIndex)), labels);
        if (ids[index] != model.GamepadIndex) { controller.StopPreview(); model.GamepadIndex = ids[index]; }
        using (new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite || model.Map is null))
        {
            if (model.Preview is null) { if (GUILayout.Button("Listen")) controller.StartPreview(); }
            else if (GUILayout.Button("Stop")) controller.StopPreview();
        }
        if (model.Preview is { } session)
            foreach (var action in session.actions.Where(action => action.map == model.Map?.name))
                GUILayout.Label(action.name + ": " + action.phase + " " + action.value, EditorStyles.wordWrappedLabel);
    }
    private bool Matches(string value) => model.Search.Length == 0 || value.Contains(model.Search, StringComparison.OrdinalIgnoreCase);
    private static Fix64 Number(string name, Fix64 value) => (Fix64)EditorGUILayout.FloatField(name, (float)value);
}

[CustomEditor(typeof(InputActionAsset))]
public sealed class InputActionAssetEditor : BAssetEditor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("Open Input Actions")) InputActionsWindow.Open((InputActionAsset)target);
        var asset = (InputActionAsset)target;
        GUILayout.Label($"{asset.maps.Count} maps • {asset.maps.Sum(map => map.actions.Count)} actions", EditorStyles.wordWrappedLabel);
        try { asset.Validate(); } catch (Exception error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
    }
}
