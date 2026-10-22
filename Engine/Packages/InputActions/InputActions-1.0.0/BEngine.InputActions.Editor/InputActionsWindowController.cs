using BEngine.Editor;

namespace BEngine.InputActions.Editor;

public sealed class InputActionsWindowModel(string id) : EditorWindowModel(id)
{
    public InputActionAsset? Asset { get; set; }
    public int MapIndex { get; set; } = -1;
    public int ActionIndex { get; set; } = -1;
    public int BindingIndex { get; set; } = -1;
    public int GamepadIndex { get; set; } = -1;
    public string Search { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public InputActionSession? Preview { get; internal set; }
    public InputRebindingOperation? Rebind { get; internal set; }
    public InputActionMapDefinition? Map => Asset?.maps.ElementAtOrDefault(MapIndex);
    public InputActionDefinition? Action => Map?.actions.ElementAtOrDefault(ActionIndex);
    public InputBinding? Binding => Action?.bindings.ElementAtOrDefault(BindingIndex);
}

public sealed class InputActionsWindowController(EditorWindowMvcContext context, InputActionsWindowModel model)
    : EditorWindowController<InputActionsWindow, InputActionsWindowModel>(context, model)
{
    private double _lastTime;
    private readonly HashSet<KeyCode> _previewKeys = [];
    public void Select(InputActionAsset? asset)
    {
        StopPreview(); Model.Asset = asset; Model.MapIndex = asset?.maps.Count > 0 ? 0 : -1;
        Model.ActionIndex = Model.Map?.actions.Count > 0 ? 0 : -1; Model.BindingIndex = Model.Action?.bindings.Count > 0 ? 0 : -1;
        Model.Message = string.Empty; View.Repaint();
    }
    public void SelectMap(int index) { StopPreview(); Model.MapIndex = index; Model.ActionIndex = Model.Map?.actions.Count > 0 ? 0 : -1; Model.BindingIndex = Model.Action?.bindings.Count > 0 ? 0 : -1; View.Repaint(); }
    public void SelectAction(int index) { StopPreview(); Model.ActionIndex = index; Model.BindingIndex = Model.Action?.bindings.Count > 0 ? 0 : -1; View.Repaint(); }
    public void SelectBinding(int index) { StopPreview(); Model.BindingIndex = index; View.Repaint(); }

    public bool Edit(string label, Action action)
    {
        EditorAssetWritePolicy.EnsureCanWrite(label);
        if (Model.Asset is not { } asset) return false;
        StopPreview(); Undo.IncrementCurrentGroup(); var group = Undo.GetCurrentGroup(); Undo.RecordObject(asset, label);
        try { action(); asset.Validate(); EditorUtility.SetDirty(asset); Model.Message = string.Empty; return true; }
        catch (Exception error) { Undo.RevertAllDownToGroup(group); Model.Message = error.Message; return false; }
        finally { View.Repaint(); }
    }
    public void AddMap() => Edit("Add input map", () =>
    {
        var asset = Model.Asset!; asset.maps.Add(new() { name = Unique("Gameplay", asset.maps.Select(map => map.name)) });
        Model.MapIndex = asset.maps.Count - 1; Model.ActionIndex = Model.BindingIndex = -1;
    });
    public void DeleteMap() => Edit("Delete input map", () =>
    { if (Model.Map is { } map) Model.Asset!.maps.Remove(map); Model.MapIndex = Math.Min(Model.MapIndex, Model.Asset!.maps.Count - 1); Model.ActionIndex = Model.BindingIndex = -1; });
    public void AddAction() => Edit("Add input action", () =>
    {
        if (Model.Map is not { } map) return;
        map.actions.Add(new() { name = Unique("Action", map.actions.Select(action => action.name)) });
        Model.ActionIndex = map.actions.Count - 1; Model.BindingIndex = -1;
    });
    public void DeleteAction() => Edit("Delete input action", () =>
    { if (Model.Map is { } map && Model.Action is { } action) map.actions.Remove(action); Model.ActionIndex = Math.Min(Model.ActionIndex, Model.Map?.actions.Count - 1 ?? -1); Model.BindingIndex = -1; });
    public void AddBinding() => Edit("Add input binding", () =>
    { if (Model.Action is { } action) { action.bindings.Add(new()); Model.BindingIndex = action.bindings.Count - 1; } });
    public void DeleteBinding() => Edit("Delete input binding", () =>
    { if (Model.Action is { } action && Model.Binding is { } binding) { action.bindings.Remove(binding); Model.BindingIndex = Math.Min(Model.BindingIndex, action.bindings.Count - 1); } });
    public void DuplicateBinding() => Edit("Duplicate input binding", () =>
    { if (Model.Binding is { } binding) { var copy = Copy(binding); copy.id = Guid.NewGuid().ToString("N"); Model.Action!.bindings.Insert(++Model.BindingIndex, copy); } });
    public void MoveBinding(int direction) => Edit("Reorder input binding", () =>
    {
        if (Model.Action is not { } action || Model.Binding is not { } binding) return;
        var index = Math.Clamp(Model.BindingIndex + direction, 0, action.bindings.Count - 1);
        action.bindings.RemoveAt(Model.BindingIndex); action.bindings.Insert(index, binding); Model.BindingIndex = index;
    });
    public void SetBinding(InputBinding binding) => Edit("Edit input binding", () =>
    { if (Model.Binding is { } current) { var copy = Copy(binding); copy.id = current.id; Model.Action!.bindings[Model.BindingIndex] = copy; } });

    public bool Save()
    {
        EditorAssetWritePolicy.EnsureCanWrite("Save input actions"); StopPreview();
        if (Model.Asset is not { } asset) return false;
        try
        {
            asset.Validate();
            if (string.IsNullOrWhiteSpace(asset.assetPath))
            {
                var folder = ProjectWindowUtil.GetActiveFolderPath() ?? "Assets";
                AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(folder.TrimEnd('/', '\\') + "/Input Actions.asset.yaml"));
            }
            EditorUtility.SetDirty(asset); AssetDatabase.SaveAssets(); Model.Message = "Saved " + asset.assetPath; return true;
        }
        catch (Exception error) { Model.Message = error.Message; return false; }
    }
    public void StartPreview()
    {
        EditorAssetWritePolicy.EnsureCanWrite("Preview authored input actions"); StopPreview();
        if (Model.Asset is not { } asset || Model.Map is not { } map) return;
        var session = new InputActionSession(asset);
        try
        {
            session.PairGamepad(Model.GamepadIndex, exclusive: false); session.EnableMap(map.name);
            Model.Preview = session; _lastTime = EditorApplication.timeSinceStartup; Model.Message = "Listening. Editing stops this preview.";
        }
        catch (Exception error) { session.Dispose(); Model.Message = error.Message; }
    }
    public void TickPreview()
    {
        if (Model.Preview is not { } session) return;
        if (!EditorAssetWritePolicy.CanWrite) { StopPreview(); return; }
        var now = EditorApplication.timeSinceStartup; var elapsed = Math.Clamp(now - _lastTime, 0, .25); _lastTime = now;
        session.Update((Fix64)elapsed); View.Repaint();
    }
    public void HandleInputEvent(Event current)
    {
        if (Model.Preview is null || !EditorAssetWritePolicy.CanWrite) return;
        var down = current.type is EventType.KeyDown or EventType.MouseDown;
        var up = current.type is EventType.KeyUp or EventType.MouseUp;
        if (!down && !up) return;
        var key = current.isKey ? current.keyCode : current.button is >= 0 and <= 6 ? (KeyCode)((int)KeyCode.Mouse0 + current.button) : KeyCode.None;
        if (key == KeyCode.None) return;
        if (down && !Input.GetKey(key)) _previewKeys.Add(key);
        Input.SetKeyState(key, down);
        if (up) _previewKeys.Remove(key);
        var capturing = Model.Rebind is { isCompleted: false };
        // Sample each native GUI event, so a down/up pair between editor Updates can be captured.
        TickPreview();
        if (capturing || current.isKey) current.Use();
    }
    public void CaptureBinding()
    {
        EditorAssetWritePolicy.EnsureCanWrite("Capture input binding");
        if (Model.Action is not { } action || Model.Binding is null) return;
        if (Model.Preview is null) StartPreview();
        if (Model.Preview is not { } session) return;
        var path = Model.Map!.name + "/" + action.name; var index = Model.BindingIndex;
        Model.Rebind = session.BeginRebind(path, index);
        Model.Rebind.completed += operation =>
        {
            if (operation.isCanceled) { Model.Message = "Capture canceled."; return; }
            var binding = session.GetBinding(path, index); SetBinding(binding); Model.Message = "Captured " + binding.control;
        };
        Model.Message = "Press a keyboard/mouse/gamepad button. Escape cancels; composites and gestures use the fields below.";
    }
    public void StopPreview()
    {
        Model.Rebind?.Cancel(); Model.Rebind = null; Model.Preview?.Dispose(); Model.Preview = null;
        foreach (var key in _previewKeys) Input.SetKeyState(key, false);
        _previewKeys.Clear();
    }
    public static InputBinding Copy(InputBinding binding) => new()
    {
        id = binding.id, scheme = binding.scheme, control = binding.control, key = binding.key, negative = binding.negative,
        up = binding.up, down = binding.down, button = binding.button, axis = binding.axis, axisY = binding.axisY,
        deadZone = binding.deadZone, scale = binding.scale, fingerId = binding.fingerId, requiredTouches = binding.requiredTouches
    };
    private static string Unique(string prefix, IEnumerable<string> names)
    { var used = names.ToHashSet(StringComparer.Ordinal); var name = prefix; for (var i = 2; used.Contains(name); i++) name = prefix + " " + i; return name; }
}
