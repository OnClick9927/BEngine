namespace BEngine.Editor;

[EditorWindowIcon("Icons/AssetAnimation.png")]
public sealed partial class AnimationWindow : UIElementsEditorWindow<AnimationWindowModel, AnimationWindowController>
{
    public AnimationWindow() { titleContent = new GUIContent("Animation"); minSize = new Vector2(460, 300); }
    protected override AnimationWindowModel CreateWindowModel(EditorWindowMvcContext context) => new(context.WindowId);
    protected override AnimationWindowController CreateWindowController(EditorWindowMvcContext context, AnimationWindowModel model) => new(context, model);
    [MenuItem("Window/Animation/Animation", false, 230)]
    public static void Open() => GetWindow<AnimationWindow>("Animation").Show();
    public static void Open(AnimationClip clip) { var window = GetWindow<AnimationWindow>("Animation"); window.controller.Select(clip); window.Show(); }
    protected override void OnSelectionChange()
    {
        if (model.Locked) return;
        if (Selection.activeObject is AnimationClip clip) { if (clip != model.Clip) controller.Select(clip); }
        else if (Selection.activeGameObject is { } target)
        {
            // Selecting an animated child keeps the root clip open while recording.
            if (model.Recording && model.Target is { } root && AnimationTimelineEditing.GetProperties(root).Any(p => p.Component.gameObject == target)) return;
            if (target == model.Target) return;
            controller.StopPreview(); model.Target = target;
            var selected = target.GetComponent<Animation>()?.clip ??
                target.GetComponent<Animator>()?.runtimeAnimatorController?.states.FirstOrDefault(state => state.clip is not null)?.clip;
            if (selected != model.Clip) controller.Select(selected);
        }
    }
    protected override void OnEnable() { OnSelectionChange(); EditorApplication.playModeStateChanged += OnPlayMode; Undo.undoRedoPerformed += OnUndo; }
    protected override void OnDisable() { controller.StopPreview(); uiElementContainer.visible = false; EditorApplication.playModeStateChanged -= OnPlayMode; Undo.undoRedoPerformed -= OnUndo; }
    protected override void OnLostFocus() { controller.EndKeyDrag(); controller.EndTangentDrag(); CancelTimelineGesture(); }
    internal void CancelTimelineGesture() { _trackCanvas?.CancelGesture(); _eventRecorded = false; }
    private void OnPlayMode(PlayModeStateChange state) => controller.StopPreview();
    private void OnUndo()
    {
        model.Selection.Clear(); model.EventIndex = -1;
        controller.EndKeyDrag(); controller.EndTangentDrag(); CancelTimelineGesture();
        model.CurveBindings.RemoveWhere(index => index < 0 || index >= (model.Clip?.bindings.Count ?? 0));
        Repaint();
    }
    protected override void Update()
    {
        controller.UpdatePreview();
        if (EditorApplication.isPlaying && controller.ObserveRuntime()) Repaint();
    }
    protected override void OnGUI()
    {
        var width = GUIUtility.currentViewWidth; var height = GUIUtility.currentViewHeight;
        if (width < 200 || height < 160) { uiElementContainer.visible = false; GUI.Label(new Rect(4, 4, width - 8, 40), "Resize the Animation window to edit the timeline."); return; }
        DrawToolbar(width);
        if (model.Clip is not { isDestroyed: false } clip)
        { uiElementContainer.visible = false; GUI.Label(new Rect(14, 105, width - 28, 60), "Select a GameObject or Animation Clip.\nCreate a clip to start animating."); return; }
        var footer = height >= 410 && (model.EventIndex >= 0 || model.Selection.Count == 1) ? 152 : 52;
        DrawTimeline(new Rect(0, 83, width, Fix64.Max(62, height - 83 - footer)), clip);
        DrawSelectionDetails(new Rect(4, height - footer + 4, width - 8, footer - 8), clip);
    }

    private void DrawToolbar(Fix64 width)
    {
        GUI.DrawRect(new Rect(0, 0, width, 81), new Color((Fix64).16, (Fix64).17, (Fix64).18));
        var clip = EditorGUI.ObjectField(new Rect(5, 4, Fix64.Max(80, width - 253), 23), model.Clip, false);
        if (!ReferenceEquals(clip, model.Clip)) controller.Select(clip);
        if (GUI.Button(new Rect(width - 242, 4, 43, 23), "New")) controller.Select(new AnimationClip { name = "New Animation" });
        using (new EditorGUI.DisabledScope(model.Clip is null))
            if (GUI.Button(new Rect(width - 195, 4, 43, 23), "Save")) controller.Save();
        using (new EditorGUI.DisabledScope(!Undo.canUndo)) if (GUI.Button(new Rect(width - 148, 4, 44, 23), "Undo")) Undo.PerformUndo();
        using (new EditorGUI.DisabledScope(!Undo.canRedo)) if (GUI.Button(new Rect(width - 100, 4, 44, 23), "Redo")) Undo.PerformRedo();
        model.Locked = GUI.Toggle(new Rect(width - 52, 4, 47, 23), model.Locked, "Lock", EditorStyles.toolbarButton);
        using (new EditorGUI.DisabledScope(model.Clip is null || model.Target is null || EditorApplication.isPlaying))
        {
            if (GUI.Toggle(new Rect(5, 31, 67, 23), model.Previewing, "Preview", EditorStyles.toolbarButton) != model.Previewing) controller.SetPreview(!model.Previewing);
            var old = GUI.backgroundColor;
            if (model.Recording) GUI.backgroundColor = new Color((Fix64).8, (Fix64).2, (Fix64).2);
            if (GUI.Toggle(new Rect(76, 31, 62, 23), model.Recording, "Record", EditorStyles.toolbarButton) != model.Recording) controller.SetRecording(!model.Recording);
            GUI.backgroundColor = old;
            if (GUI.Button(new Rect(142, 31, 28, 23), new GUIContent("|<", "Go to first frame"))) controller.SetTime(0);
            if (GUI.Button(new Rect(174, 31, 26, 23), new GUIContent("<", "Go to previous keyframe"))) controller.SeekKey(-1);
            if (GUI.Button(new Rect(204, 31, 40, 23), model.Playing ? "Pause" : "Play")) controller.TogglePreview();
            if (GUI.Button(new Rect(248, 31, 26, 23), new GUIContent(">", "Go to next keyframe"))) controller.SeekKey(1);
            if (GUI.Button(new Rect(278, 31, 28, 23), new GUIContent(">|", "Go to last frame"))) controller.SetTime(model.Clip?.length ?? 0);
        }
        GUI.Label(new Rect(314, 33, 37, 20), "Frame", EditorStyles.miniLabel);
        if (model.Clip is { } asset)
        {
            var frame = Mathf.RoundToInt(model.Time * AnimationTimelineEditing.Rate(asset));
            var next = EditorGUI.IntField(new Rect(352, 31, 57, 23), frame);
            if (frame != next) controller.SetTime((Fix64)Math.Clamp(next, 0, 20736000) / AnimationTimelineEditing.Rate(asset));
            if (width >= 540)
            {
                GUI.Label(new Rect(420, 33, 54, 20), "Samples", EditorStyles.miniLabel);
                var displayedRate = (int)AnimationTimelineEditing.Rate(asset);
                var samples = Math.Clamp(EditorGUI.IntField(new Rect(475, 31, 55, 23), displayedRate), 1, 240);
                if (samples != displayedRate) AnimationEditing.Edit(asset, "Set Animation Sample Rate", () => asset.frameRate = samples);
            }
        }
        var targetWidth = Fix64.Max(140, width - 255);
        GUI.Label(new Rect(5, 59, 45, 18), "Target", EditorStyles.miniLabel);
        var target = EditorGUI.ObjectField(new Rect(52, 58, targetWidth - 47, 20), model.Target, true);
        if (target != model.Target) { controller.StopPreview(); model.Target = target; }
        if (EditorApplication.isPlaying && model.RuntimeLabel.Length > 0)
            GUI.Label(new Rect(width - 235, 36, 230, 18), model.RuntimeLabel, EditorStyles.miniLabel);
        if (model.Clip is { } settings)
        {
            var wrap = (WrapMode)EditorGUI.EnumPopup(new Rect(width - 243, 58, 132, 20), "", settings.wrapMode);
            if (wrap != settings.wrapMode) AnimationEditing.Edit(settings, "Set Animation Wrap Mode", () => settings.wrapMode = wrap);
            if (GUI.Button(new Rect(width - 106, 58, 101, 20),
                    new GUIContent("Frame All", "Frame the full clip (A)"))) FrameAll(settings, width);
        }
    }

    // FloatField formats a shortened display string; an idle repaint must not
    // turn that formatting into a numeric edit of a fixed-point key.
    private static float TimelineFloatField(Rect area, float value)
    {
        EditorGUI.BeginChangeCheck(); var result = EditorGUI.FloatField(area, value);
        return EditorGUI.EndChangeCheck() ? result : value;
    }
    private static float TimelineFloatField(Rect area, string label, float value)
    {
        EditorGUI.BeginChangeCheck(); var result = EditorGUI.FloatField(area, label, value);
        return EditorGUI.EndChangeCheck() ? result : value;
    }

    private void ShowProperties(Rect anchor)
    {
        var menu = new GenericMenu();
        if (model.Target is not { isDestroyed: false } target) menu.AddDisabledItem(new GUIContent("Select a target GameObject"));
        else foreach (var property in AnimationTimelineEditing.GetProperties(target))
        {
            var added = model.Clip?.bindings.Any(property.Matches) == true;
            if (added) menu.AddDisabledItem(new GUIContent(property.DisplayName), true);
            else menu.AddItem(new GUIContent(property.DisplayName), false, () => controller.AddProperty(property));
        }
        menu.ShowAsAdvancedDropdown(anchor);
    }

    private void DrawSelectionDetails(Rect area, AnimationClip clip)
    {
        if (model.EventIndex >= 0 && model.EventIndex < clip.events.Count)
        {
            var evt = clip.events[model.EventIndex];
            GUI.Label(new Rect(area.x, area.y, 95, 20), "Animation Event", EditorStyles.boldLabel);
            var function = EditorGUI.TextField(new Rect(area.x + 102, area.y, Fix64.Max(70, area.width - 278), 22), evt.functionName);
            var frame = EditorGUI.IntField(new Rect(area.xMax - 166, area.y, 65, 22), Mathf.RoundToInt(evt.time * AnimationTimelineEditing.Rate(clip)));
            if (function != evt.functionName || frame != Mathf.RoundToInt(evt.time * AnimationTimelineEditing.Rate(clip)))
                AnimationEditing.Edit(clip, "Edit Animation Event", () => { evt.functionName = function; evt.time = AnimationTimelineEditing.SnapTime(clip, (Fix64)frame / AnimationTimelineEditing.Rate(clip)); clip.events.Sort((a, b) => a.time.CompareTo(b.time)); model.EventIndex = clip.events.IndexOf(evt); });
            if (GUI.Button(new Rect(area.xMax - 95, area.y, 95, 22), "Delete Event")) controller.DeleteEvent();
            if (area.height > 70)
            {
                var text = EditorGUI.TextField(new Rect(area.x, area.y + 26, area.width / 2 - 4, 22), evt.stringParameter);
                var integer = EditorGUI.IntField(new Rect(area.x + area.width / 2, area.y + 26, area.width / 4 - 4, 22), evt.intParameter);
                var number = TimelineFloatField(new Rect(area.x + area.width * 3 / 4, area.y + 26, area.width / 4, 22), (float)evt.floatParameter);
                if (text != evt.stringParameter || integer != evt.intParameter || number != (float)evt.floatParameter)
                    AnimationEditing.Edit(clip, "Edit Animation Event Arguments", () => { evt.stringParameter = text; evt.intParameter = integer; evt.floatParameter = (Fix64)number; });
            }
        }
        else
        {
            GUI.Label(new Rect(area.x, area.y, 170, 20), $"Keyframes: {model.Selection.Count} selected", EditorStyles.boldLabel);
            if (GUI.Button(new Rect(area.xMax - 265, area.y, 82, 22),
                    new GUIContent("Add Key", "Set a key at the cursor (K)"))) controller.SetKeyAtCursor();
            if (GUI.Button(new Rect(area.xMax - 179, area.y, 86, 22), "Add Event")) controller.AddEvent();
            using (new EditorGUI.DisabledScope(model.Selection.Count == 0))
                if (GUI.Button(new Rect(area.xMax - 89, area.y, 89, 22), "Delete Keys")) controller.DeleteSelectedKeys();
            if (area.height > 70 && model.Selection.Count == 1)
            {
                var selected = model.Selection.First();
                if ((uint)selected.Binding < (uint)clip.bindings.Count && clip.bindings[selected.Binding].curve.keys.FirstOrDefault(k => k.time == selected.Time) is var key)
                {
                    DrawKeyDetails(new Rect(area.x, area.y + 26, area.width, area.height - 26), clip, selected, key);
                }
            }
        }
        if (model.Message.Length > 0)
            GUI.Label(new Rect(area.x, area.yMax - 20, area.width, 20), model.Message, EditorStyles.miniLabel);
    }
}

public sealed class AnimationWindowModel(string windowId) : EditorWindowModel(windowId)
{
    public AnimationClip? Clip { get; internal set; }
    public GameObject? Target { get; internal set; }
    public Fix64 Time { get; internal set; }
    public bool Playing { get; internal set; }
    public bool Previewing { get; internal set; }
    public bool Recording { get; internal set; }
    public string RuntimeLabel { get; internal set; } = string.Empty;
    public IReadOnlyCollection<AnimationKeySelection> SelectedKeys => Selection;
    internal readonly HashSet<AnimationKeySelection> Selection = [];
    internal readonly HashSet<int> CurveBindings = [];
    internal bool Locked, Curves;
    internal Fix64 PixelsPerSecond = 200, StartTime, VerticalScroll;
    internal int BindingIndex, EventIndex = -1;
    internal string Path = "", Component = "BEngine.Transform", Property = "localPosition.x", Message = "";
}

public sealed class AnimationWindowController : EditorWindowController<AnimationWindow, AnimationWindowModel>
{
    private readonly AnimationPreviewSession _preview;
    private string _lastRuntimeLabel = string.Empty;
    private double _lastTime;
    private List<(AnimationKeySelection Selection, Keyframe Key)>? _dragKeys;
    private Dictionary<int, Keyframe[]>? _dragCurves;
    private bool _dragRecorded;
    private AnimationKeySelection? _tangentSelection;
    private Keyframe _tangentOriginal;
    private bool _tangentRecorded;
    private static readonly List<(string Path, string Component, string Property, AnimationBindingKind Kind, string ObjectType, BObject? Reference, Keyframe Key)> Clipboard = [];
    public AnimationWindowController(EditorWindowMvcContext context, AnimationWindowModel model) : base(context, model)
    { _preview = new(() => { Model.Previewing = false; Model.Playing = false; Model.Recording = false; }, CaptureRecording); }
    public void Select(AnimationClip? clip)
    {
        StopPreview(); View.CancelTimelineGesture(); Model.Clip = clip; Model.BindingIndex = 0; Model.Time = 0; Model.StartTime = 0;
        Model.Selection.Clear(); Model.CurveBindings.Clear();
        if (clip?.bindings.Count > 0) Model.CurveBindings.Add(0);
        Model.EventIndex = -1; Model.Message = ""; Repaint();
    }
    public void AddProperty(AnimationProperty property)
    {
        if (Model.Clip is not { isDestroyed: false } clip || property.Component.isDestroyed) return;
        var binding = AnimationTimelineEditing.AddProperty(clip, property, Model.Time);
        Model.BindingIndex = clip.bindings.IndexOf(binding);
        Model.CurveBindings.Clear(); Model.CurveBindings.Add(Model.BindingIndex); Repaint();
    }
    // Retained for callers that already supply a validated runtime binding.
    public void AddBinding()
    {
        if (Model.Clip is not { } clip) return;
        if (Model.Target is { } target)
        {
            var property = AnimationTimelineEditing.GetProperties(target).FirstOrDefault(p => p.RelativePath == Model.Path && p.ComponentType == Model.Component && p.PropertyName == Model.Property);
            if (property is not null) { AddProperty(property); return; }
        }
        Model.Message = "Use Add Property to choose a supported property on the target object.";
    }
    public void Save()
    {
        CaptureRecording(); StopPreview();
        try { if (Model.Clip is { } clip) Model.Message = "Saved " + AnimationEditing.Save(clip); }
        catch (Exception exception) { Model.Message = exception.Message; }
    }
    public void Sample()
    {
        if (Model.Clip is not { isDestroyed: false } clip || Model.Target is not { isDestroyed: false } target || EditorApplication.isPlaying) return;
        Model.Previewing = true; _preview.Sample(clip, target, Model.Time); Repaint();
    }
    public void SetPreview(bool enabled) { if (!enabled) StopPreview(); else Sample(); }
    public void SetRecording(bool enabled)
    {
        if (Model.Clip is not { isDestroyed: false } clip || Model.Target is not { isDestroyed: false } target || EditorApplication.isPlaying) return;
        if (!enabled) CaptureRecording();
        Model.Playing = false; Model.Recording = enabled; _preview.SetRecording(clip, target, enabled); Sample();
    }
    public void SetTime(Fix64 time)
    {
        CaptureRecording();
        if (Model.Clip is not { } clip) return;
        Model.Time = AnimationTimelineEditing.SnapTime(clip, time); Sample(); Repaint();
    }
    public void Step(int direction) { if (Model.Clip is { } clip) SetTime(Model.Time + (Fix64)direction / AnimationTimelineEditing.Rate(clip)); }
    public void SeekKey(int direction)
    {
        if (Model.Clip is not { } clip) return;
        var times = clip.bindings.SelectMany(b => b.curve.keys).Select(key => key.time).Distinct().Order().ToArray();
        var next = direction < 0 ? times.Where(time => time < Model.Time).DefaultIfEmpty(0).Last() : times.Where(time => time > Model.Time).DefaultIfEmpty(clip.length).First();
        SetTime(next);
    }
    public void TogglePreview()
    {
        if (Model.Clip is not { isDestroyed: false } || Model.Target is not { isDestroyed: false } || EditorApplication.isPlaying) return;
        if (Model.Recording) SetRecording(false);
        Model.Playing = !Model.Playing; _lastTime = EditorApplication.timeSinceStartup; Sample();
    }
    public void StopPreview()
    {
        CaptureRecording(); EndKeyDrag(); EndTangentDrag();
        _preview.Dispose(); SceneView.RepaintAll(); Repaint();
    }

    internal bool ObserveRuntime()
    {
        if (!EditorApplication.isPlaying || Model.Target is not { isDestroyed: false } target)
        {
            if (Model.RuntimeLabel.Length == 0) return false;
            Model.RuntimeLabel = string.Empty; _lastRuntimeLabel = string.Empty; return true;
        }
        var animation = target.GetComponent<Animation>();
        var animator = target.GetComponent<Animator>();
        var label = animation is { isPlaying: true } ?
            $"Runtime  {animation.time:0.###} s" :
            animator is { } state ?
            $"Runtime  {state.currentStateName}  {state.normalizedTime:0.##}" : string.Empty;
        if (label == _lastRuntimeLabel) return false;
        _lastRuntimeLabel = label; Model.RuntimeLabel = label; return true;
    }
    public void UpdatePreview()
    {
        if (!Model.Previewing) return;
        if (Model.Clip is not { isDestroyed: false } clip || Model.Target is not { isDestroyed: false } || EditorApplication.isPlaying) { StopPreview(); return; }
        CaptureRecording();
        if (!Model.Playing) return;
        var now = EditorApplication.timeSinceStartup;
        Model.Time += (Fix64)Math.Clamp(now - _lastTime, 0, .1); _lastTime = now;
        if (clip.length > 0 && Model.Time > clip.length) Model.Time %= clip.length;
        Sample();
    }
    public void CaptureRecording()
    {
        if (!Model.Recording || Model.Clip is not { isDestroyed: false } clip) return;
        var changes = _preview.Changes(); if (changes.Count == 0) return;
        var time = AnimationTimelineEditing.SnapTime(clip, Model.Time);
        AnimationEditing.Edit(clip, "Record Animation Keys", () =>
        {
            foreach (var (property, value) in changes)
            {
                var binding = clip.bindings.FirstOrDefault(property.Matches);
                if (binding is null)
                { binding = property.CreateBinding();
                    if (time > 0) binding.SetValue(0, _preview.Original(property));
                    clip.bindings.Add(binding); }
                binding.SetValue(time, value);
            }
        });
        _preview.Sample(clip, Model.Target!, Model.Time); Repaint();
    }
    public void SelectKey(int binding, Fix64 time, bool additive = false)
    {
        if (!additive) Model.Selection.Clear();
        var key = new AnimationKeySelection(binding, time);
        if (!Model.Selection.Add(key) && additive) Model.Selection.Remove(key);
        Model.BindingIndex = binding;
        if (!additive) Model.CurveBindings.Clear();
        Model.CurveBindings.Add(binding); Model.EventIndex = -1; Repaint();
    }
    public void SetKeyAtCursor()
    {
        if (Model.Clip is not { } clip) return;
        var indices = Model.CurveBindings.Count > 0 ? Model.CurveBindings.Order().ToArray() : [Model.BindingIndex];
        var selected = indices.Where(index => (uint)index < (uint)clip.bindings.Count).ToArray();
        if (selected.Length == 0) return;
        var time = AnimationTimelineEditing.SnapTime(clip, Model.Time);
        var properties = Model.Target is { } target ? AnimationTimelineEditing.GetProperties(target) : [];
        AnimationEditing.Edit(clip, "Set Animation Keys", () =>
        {
            foreach (var index in selected)
            {
                var binding = clip.bindings[index];
                var property = properties.FirstOrDefault(item => item.Matches(binding));
                binding.SetValue(time, property?.Read() ?? binding.Evaluate(time));
            }
        });
        Model.Selection.Clear();
        foreach (var index in selected) Model.Selection.Add(new(index, time));
        Model.BindingIndex = selected[0]; Model.EventIndex = -1; Sample(); Repaint();
    }
    public void SetBindingValue(int index, Fix64 value)
    {
        if (Model.Clip is not { } clip || (uint)index >= (uint)clip.bindings.Count) return;
        var time = AnimationTimelineEditing.SnapTime(clip, Model.Time);
        var binding = clip.bindings[index];
        if (binding.kind == AnimationBindingKind.ObjectReference) return;
        AnimationEditing.Edit(clip, "Set Animation Key", () => binding.SetValue(time, new(value))); SelectKey(index, time); Sample();
    }
    public void SetObjectReferenceValue(int index, Fix64 time, BObject? value)
    {
        if (Model.Clip is not { } clip || (uint)index >= (uint)clip.bindings.Count) return;
        var binding = clip.bindings[index];
        if (binding.kind != AnimationBindingKind.ObjectReference) return;
        var type = RuntimeTypeCache.FindType(binding.objectType) ?? typeof(BObject);
        if (value is not null && !type.IsInstanceOfType(value)) throw new ArgumentException("The object does not match this animation track.");
        time = AnimationTimelineEditing.SnapTime(clip, time);
        AnimationEditing.Edit(clip, "Set Object Animation Key", () => binding.SetObjectReferenceKey(time, value));
        SelectKey(index, time); Sample(); Repaint();
    }
    public void EditSelectedTangentModes(AnimationTangentMode input, AnimationTangentMode output, WeightedMode weights, Fix64 inWeight, Fix64 outWeight)
    {
        if (Model.Clip is not { } clip || Model.Selection.Count != 1) return;
        var selected = Model.Selection.First();
        if ((uint)selected.Binding >= (uint)clip.bindings.Count || clip.bindings[selected.Binding].kind != AnimationBindingKind.Numeric) return;
        var curve = clip.bindings[selected.Binding].curve;
        var key = curve.keys.FirstOrDefault(key => key.time == selected.Time);
        AnimationEditing.Edit(clip, "Edit Animation Tangent Modes", () => curve.AddKey(key with
        { inMode = input, outMode = output, weightedMode = weights, inWeight = inWeight, outWeight = outWeight }));
        Sample(); Repaint();
    }
    public void DeleteSelectedKeys()
    {
        if (Model.Clip is not { } clip) return;
        AnimationTimelineEditing.DeleteKeys(clip, Model.Selection); Model.Selection.Clear(); Sample(); Repaint();
    }
    public void EditSelectedKey(Fix64 time, Fix64 value, Fix64 input, Fix64 output)
    {
        if (Model.Clip is not { } clip || Model.Selection.Count != 1) return;
        var selected = Model.Selection.First(); if ((uint)selected.Binding >= (uint)clip.bindings.Count) return;
        var curve = clip.bindings[selected.Binding].curve; time = AnimationTimelineEditing.SnapTime(clip, time);
        var original = curve.keys.FirstOrDefault(key => key.time == selected.Time);
        var binding = clip.bindings[selected.Binding];
        if (binding.kind == AnimationBindingKind.ObjectReference) value = original.value;
        else if (binding.kind == AnimationBindingKind.Boolean) value = value == 0 ? 0 : 1;
        AnimationEditing.Edit(clip, "Edit Animation Key", () =>
        {
            curve.keys = curve.keys.Where(key => key.time != selected.Time).ToArray();
            AnimationTimelineEditing.Upsert(curve, original with
            {
                time = time, value = value, inTangent = input, outTangent = output,
                inMode = input == original.inTangent ? original.inMode : AnimationTangentMode.Free,
                outMode = output == original.outTangent ? original.outMode : AnimationTangentMode.Free
            });
        });
        SelectKey(selected.Binding, time); Sample();
    }
    public void BeginKeyDrag()
    {
        if (Model.Clip is not { } clip) return;
        _dragCurves = Model.Selection.Select(key => key.Binding).Distinct().Where(index => (uint)index < (uint)clip.bindings.Count).ToDictionary(index => index, index => clip.bindings[index].curve.keys);
        _dragKeys = Model.Selection.Where(key => _dragCurves.ContainsKey(key.Binding) && _dragCurves[key.Binding].Any(frame => frame.time == key.Time)).Select(key => (key, _dragCurves[key.Binding].First(frame => frame.time == key.Time))).ToList();
        _dragRecorded = false;
    }
    public void DragKeys(Fix64 timeOffset, Fix64 valueOffset = default)
    {
        if (!EditorAssetWritePolicy.CanWrite) return;
        if (_dragKeys is not { Count: > 0 } || _dragCurves is null || Model.Clip is not { } clip) return;
        timeOffset = Mathf.Round(timeOffset * AnimationTimelineEditing.Rate(clip)) / AnimationTimelineEditing.Rate(clip);
        timeOffset = Fix64.Max(timeOffset, -_dragKeys.Min(item => item.Key.time));
        if (!_dragRecorded && timeOffset == 0 && valueOffset == 0) return;
        if (!_dragRecorded) { Undo.IncrementCurrentGroup(); Undo.RecordObject(clip, "Move Animation Keys"); _dragRecorded = true; }
        Model.Selection.Clear();
        foreach (var (index, keys) in _dragCurves)
        {
            if ((uint)index >= (uint)clip.bindings.Count) continue;
            var selectedTimes = _dragKeys.Where(item => item.Selection.Binding == index).Select(item => item.Key.time).ToHashSet();
            clip.bindings[index].curve.keys = keys.Where(key => !selectedTimes.Contains(key.time)).ToArray();
        }
        foreach (var (selected, key) in _dragKeys)
        {
            if ((uint)selected.Binding >= (uint)clip.bindings.Count) continue;
            var time = key.time + timeOffset;
            AnimationTimelineEditing.Upsert(clip.bindings[selected.Binding].curve, key with { time = time, value = key.value + (clip.bindings[selected.Binding].kind == AnimationBindingKind.Numeric ? valueOffset : 0) });
            Model.Selection.Add(new(selected.Binding, time));
        }
        EditorUtility.SetDirty(clip); Sample(); Repaint();
    }
    public void EndKeyDrag() { _dragKeys = null; _dragCurves = null; _dragRecorded = false; }
    public void BeginTangentDrag(object id)
    {
        EndTangentDrag();
        if (id is not AnimationKeySelection selected || Model.Clip is not { } clip ||
            (uint)selected.Binding >= (uint)clip.bindings.Count) return;
        var keys = clip.bindings[selected.Binding].curve.keys;
        var index = Array.FindIndex(keys, key => key.time == selected.Time);
        if (index < 0) return;
        if (clip.bindings[selected.Binding].kind != AnimationBindingKind.Numeric) return;
        _tangentSelection = selected;
        _tangentOriginal = keys[index];
        SelectKey(selected.Binding, selected.Time);
    }
    public void DragTangent(object id, bool input, Fix64 slopeOffset)
    {
        if (!EditorAssetWritePolicy.CanWrite) return;
        if (id is not AnimationKeySelection selected || _tangentSelection != selected ||
            Model.Clip is not { } clip || (uint)selected.Binding >= (uint)clip.bindings.Count) return;
        var curve = clip.bindings[selected.Binding].curve;
        var keys = curve.keys;
        var index = Array.FindIndex(keys, key => key.time == selected.Time);
        if (index < 0) return;
        var tangent = (input ? _tangentOriginal.inTangent : _tangentOriginal.outTangent) + slopeOffset;
        if (tangent == (input ? keys[index].inTangent : keys[index].outTangent)) return;
        if (!_tangentRecorded)
        {
            Undo.IncrementCurrentGroup(); Undo.RecordObject(clip, "Edit Animation Tangent");
            _tangentRecorded = true;
        }
        curve.MoveKey(index, keys[index] with
        {
            inTangent = input ? tangent : keys[index].inTangent,
            outTangent = input ? keys[index].outTangent : tangent,
            inMode = input ? AnimationTangentMode.Free : keys[index].inMode,
            outMode = input ? keys[index].outMode : AnimationTangentMode.Free
        });
        EditorUtility.SetDirty(clip); Sample(); Repaint();
    }
    public void EndTangentDrag()
    {
        _tangentSelection = null;
        _tangentRecorded = false;
    }
    public void CopySelectedKeys()
    {
        Clipboard.Clear(); if (Model.Clip is not { } clip || Model.Selection.Count == 0) return;
        var origin = Model.Selection.Min(key => key.Time);
        foreach (var selected in Model.Selection.OrderBy(key => key.Binding).ThenBy(key => key.Time))
        {
            if ((uint)selected.Binding >= (uint)clip.bindings.Count) continue;
            var binding = clip.bindings[selected.Binding];
            foreach (var key in binding.curve.keys.Where(key => key.time == selected.Time))
                Clipboard.Add((binding.relativePath, binding.componentType, binding.propertyName, binding.kind, binding.objectType,
                    binding.kind == AnimationBindingKind.ObjectReference ? binding.ObjectAt(key.value) : null, key with { time = key.time - origin }));
        }
        Model.Message = $"Copied {Clipboard.Count} keyframes. Paste at the current frame.";
    }
    public void PasteKeys()
    {
        if (Model.Clip is not { } clip || Clipboard.Count == 0) return;
        Model.Selection.Clear();
        AnimationEditing.Edit(clip, "Paste Animation Keys", () =>
        {
            foreach (var copied in Clipboard)
            {
                var binding = clip.bindings.FirstOrDefault(b => b.relativePath == copied.Path && b.componentType == copied.Component && b.propertyName == copied.Property);
                if (binding is null) { binding = new AnimationBinding { relativePath = copied.Path, componentType = copied.Component, propertyName = copied.Property, kind = copied.Kind, objectType = copied.ObjectType }; clip.bindings.Add(binding); }
                var time = AnimationTimelineEditing.SnapTime(clip, Model.Time + copied.Key.time);
                if (binding.kind != copied.Kind) continue;
                if (copied.Kind == AnimationBindingKind.ObjectReference) binding.SetObjectReferenceKey(time, copied.Reference);
                else AnimationTimelineEditing.Upsert(binding.curve, copied.Key with { time = time });
                Model.Selection.Add(new(clip.bindings.IndexOf(binding), time));
            }
        });
        Sample(); Repaint();
    }
    public void AddEvent()
    {
        if (Model.Clip is not { } clip) return;
        var evt = new AnimationEvent { time = AnimationTimelineEditing.SnapTime(clip, Model.Time), functionName = "OnAnimationEvent" };
        AnimationEditing.Edit(clip, "Add Animation Event", () => clip.AddEvent(evt)); Model.EventIndex = clip.events.IndexOf(evt); Model.Selection.Clear(); Repaint();
    }
    public void DeleteEvent()
    {
        if (Model.Clip is not { } clip || (uint)Model.EventIndex >= (uint)clip.events.Count) return;
        AnimationEditing.Edit(clip, "Delete Animation Event", () => clip.events.RemoveAt(Model.EventIndex)); Model.EventIndex = -1; Repaint();
    }
}
