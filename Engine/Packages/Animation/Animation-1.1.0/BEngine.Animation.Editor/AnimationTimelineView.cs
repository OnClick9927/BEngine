using BEngine.UIElements;

namespace BEngine.Editor;

public sealed partial class AnimationWindow
{
    private const int RowHeight = 31;
    private bool _eventRecorded;
    private Fix64 _eventStart;
    private TrackEditorCanvas? _trackCanvas;
    private bool _trackCanvasEvents;

    private void FrameAll(AnimationClip clip, Fix64 width)
    {
        model.StartTime = 0;
        model.PixelsPerSecond = Fix64.Clamp((width - SidebarWidth(width) - 30) / Fix64.Max((Fix64).25, clip.length + (Fix64).15), 12, 3000);
        model.Message = ""; Repaint();
    }
    private void FrameSelection(AnimationClip clip, Fix64 width)
    {
        var times = model.Selection.Select(key => key.Time).ToArray();
        if (times.Length == 0)
            times = clip.bindings.Select((binding, index) => (binding, index))
                .Where(item => model.CurveBindings.Contains(item.index))
                .SelectMany(item => item.binding.curve.keys.Select(key => key.time)).ToArray();
        if (times.Length == 0) { FrameAll(clip, width); return; }
        var first = times.Min(); var last = times.Max();
        var available = Fix64.Max(100, width - SidebarWidth(width) - 50);
        model.PixelsPerSecond = Fix64.Clamp(available / Fix64.Max((Fix64).25, last - first + (Fix64).2), 12, 3000);
        model.StartTime = Fix64.Max(0, first - (Fix64)25 / model.PixelsPerSecond);
        Repaint();
    }
    private static Fix64 SidebarWidth(Fix64 width) => Fix64.Clamp(width * (Fix64).29, 170, 290);

    private void DrawTimeline(Rect area, AnimationClip clip)
    {
        var sidebarWidth = SidebarWidth(area.width);
        var sidebar = new Rect(area.x, area.y, sidebarWidth, area.height);
        var graph = new Rect(area.x + sidebarWidth, area.y,
            Fix64.Max(1, area.width - sidebarWidth), Fix64.Max(1, area.height - 24));
        var bottom = new Rect(area.x, area.yMax - 24, sidebarWidth, 24);
        GUI.DrawRect(sidebar, new Color((Fix64).18, (Fix64).19, (Fix64).2));
        GUI.DrawRect(new Rect(sidebar.xMax - 1, area.y, 1, area.height),
            new Color((Fix64).35, (Fix64).36, (Fix64).38));
        if (GUI.Button(new Rect(area.x + 3, area.y + 2, sidebarWidth - 6, 21), "Add Property"))
            ShowProperties(new Rect(area.x, area.y, sidebarWidth, 24));
        GUI.Label(new Rect(area.x + 8, area.y + 24, sidebarWidth - 10, 20), "Events", EditorStyles.miniLabel);
        var rows = new Rect(area.x, area.y + 45, sidebarWidth, Fix64.Max(1, area.height - 70));
        model.VerticalScroll = Fix64.Clamp(model.VerticalScroll, 0,
            Fix64.Max(0, clip.bindings.Count * RowHeight - rows.height));
        if (Event.current.type == EventType.ScrollWheel && rows.Contains(Event.current.mousePosition))
        {
            model.VerticalScroll = Fix64.Clamp(model.VerticalScroll + Event.current.delta.y * 22,
                0, Fix64.Max(0, clip.bindings.Count * RowHeight - rows.height));
            Event.current.Use(); Repaint();
        }
        GUI.BeginClip(rows);
        try
        {
            for (var index = 0; index < clip.bindings.Count; index++)
            {
                var binding = clip.bindings[index];
                var y = rows.y + index * RowHeight - model.VerticalScroll;
                if (y + RowHeight < rows.y || y > rows.yMax) continue;
                var name = (binding.relativePath.Length == 0 ? model.Target?.name ?? "Root" : binding.relativePath) +
                    " / " + binding.componentType.Split('.').Last();
                var row = new Rect(rows.x + 2, y, sidebarWidth - 4, RowHeight - 1);
                if (model.CurveBindings.Contains(index))
                    GUI.DrawRect(row, new Color((Fix64).24, (Fix64).35, (Fix64).47));
                GUI.Label(new Rect(row.x + 5, y, row.width - 67, 14), name, EditorStyles.miniLabel);
                GUI.Label(new Rect(row.x + 14, y + 13, row.width - 75, 16),
                    binding.propertyName, EditorStyles.miniLabel);
                var valueArea = new Rect(row.xMax - 59, y + 5, 55, 22);
                if (binding.kind == AnimationBindingKind.ObjectReference)
                {
                    var value = binding.Evaluate(model.Time).Reference;
                    var changed = EditorGUI.ObjectField(valueArea, value, RuntimeTypeCache.FindType(binding.objectType) ?? typeof(BObject), false);
                    if (!ReferenceEquals(value, changed)) controller.SetObjectReferenceValue(index, model.Time, changed);
                }
                else if (binding.kind == AnimationBindingKind.Boolean)
                {
                    var value = binding.Evaluate(model.Time).Number != 0;
                    var changed = GUI.Toggle(valueArea, value, "");
                    if (value != changed) controller.SetBindingValue(index, changed ? 1 : 0);
                }
                else
                {
                    var value = (float)binding.curve.Evaluate(model.Time);
                    var changed = TimelineFloatField(valueArea, value);
                    if (changed != value) controller.SetBindingValue(index, (Fix64)changed);
                }
                if (Event.current.type != EventType.MouseDown || !row.Contains(Event.current.mousePosition))
                    continue;
                model.BindingIndex = index; model.EventIndex = -1;
                if (Event.current.control || Event.current.shift)
                {
                    if (!model.CurveBindings.Add(index)) model.CurveBindings.Remove(index);
                }
                else { model.CurveBindings.Clear(); model.CurveBindings.Add(index); }
                if (Event.current.button == 1)
                {
                    var selected = binding;
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Remove Property"), false, () =>
                    {
                        var removed = clip.bindings.IndexOf(selected);
                        AnimationEditing.Edit(clip, "Remove Animated Property", () => clip.bindings.Remove(selected));
                        model.Selection.Clear(); model.CurveBindings.Clear();
                        model.BindingIndex = Math.Clamp(removed, 0, Math.Max(0, clip.bindings.Count - 1));
                        if (clip.bindings.Count > 0) model.CurveBindings.Add(model.BindingIndex);
                        Repaint();
                    });
                    menu.AddItem(new GUIContent("Add Key"), false, controller.SetKeyAtCursor);
                    menu.ShowAsContext();
                }
                Event.current.Use(); Repaint();
            }
            if (clip.bindings.Count == 0)
                GUI.Label(new Rect(rows.x + 8, rows.y + 12, sidebarWidth - 16, 45),
                    "No animated properties.\nUse Add Property or Record.", EditorStyles.miniLabel);
        }
        finally { GUI.EndClip(); }
        if (GUI.Toggle(new Rect(bottom.x + 3, bottom.y + 2, sidebarWidth / 2 - 4, 21),
                !model.Curves, "Dopesheet", EditorStyles.toolbarButton) && model.Curves) model.Curves = false;
        if (GUI.Toggle(new Rect(bottom.x + sidebarWidth / 2 + 1, bottom.y + 2,
                sidebarWidth / 2 - 4, 21), model.Curves, "Curves", EditorStyles.toolbarButton) && !model.Curves)
            model.Curves = true;
        var nav = new Rect(graph.x + 5, area.yMax - 20, Fix64.Max(30, graph.width - 66), 18);
        var viewDuration = Fix64.Max((Fix64).1, graph.width / model.PixelsPerSecond);
        var range = Fix64.Max(viewDuration, Fix64.Max(clip.length * 2, model.StartTime + viewDuration));
        model.StartTime = GUI.HorizontalSlider(nav, model.StartTime, 0, range);
        GUI.Label(new Rect(area.xMax - 57, area.yMax - 20, 55, 18), "Seconds", EditorStyles.miniLabel);
        ConfigureTrackCanvas(graph, clip);
    }

    private void ConfigureTrackCanvas(Rect area, AnimationClip clip)
    {
        _trackCanvas ??= new TrackEditorCanvas();
        if (!_trackCanvasEvents)
        {
            _trackCanvasEvents = true;
            _trackCanvas.timeChanged += time => controller.SetTime(time);
            _trackCanvas.viewChanged += (start, pixels, scroll) =>
            {
                model.StartTime = start; model.PixelsPerSecond = pixels; model.VerticalScroll = scroll;
                Repaint();
            };
            _trackCanvas.keySelected += (id, additive) =>
            {
                if (id is AnimationKeySelection key) controller.SelectKey(key.Binding, key.Time, additive);
                Repaint();
            };
            _trackCanvas.keyDragStarted += controller.BeginKeyDrag;
            _trackCanvas.keysDragged += (deltaTime, deltaValue) => controller.DragKeys(deltaTime, deltaValue);
            _trackCanvas.keyDragEnded += controller.EndKeyDrag;
            _trackCanvas.tangentDragStarted += controller.BeginTangentDrag;
            _trackCanvas.tangentDragged += controller.DragTangent;
            _trackCanvas.tangentDragEnded += controller.EndTangentDrag;
            _trackCanvas.markerSelected += (id, _) =>
            {
                if (id is AnimationEvent animationEvent && model.Clip is { } activeClip)
                {
                    model.EventIndex = activeClip.events.IndexOf(animationEvent);
                    model.Selection.Clear();
                    _eventStart = animationEvent.time;
                    _eventRecorded = false;
                    Repaint();
                }
            };
            _trackCanvas.markerDragged += (id, delta) =>
            {
                if (!EditorAssetWritePolicy.CanWrite || id is not AnimationEvent animationEvent || model.Clip is not { } activeClip ||
                    !activeClip.events.Contains(animationEvent)) return;
                var time = AnimationTimelineEditing.SnapTime(activeClip, _eventStart + delta);
                if (time == animationEvent.time) return;
                if (!_eventRecorded)
                {
                    Undo.IncrementCurrentGroup();
                    Undo.RecordObject(activeClip, "Move Animation Event");
                    _eventRecorded = true;
                }
                animationEvent.time = time;
                activeClip.events.Sort((left, right) => left.time.CompareTo(right.time));
                model.EventIndex = activeClip.events.IndexOf(animationEvent);
                EditorUtility.SetDirty(activeClip);
                Repaint();
            };
            _trackCanvas.gestureEnded += () => _eventRecorded = false;
            _trackCanvas.contextRequested += point =>
            {
                if (model.Clip is not { } activeClip) return;
                var time = model.StartTime + (point.x - 10) / model.PixelsPerSecond;
                controller.SetTime(time);
                var eventIndex = point.y >= 24 && point.y < 45
                    ? activeClip.events.FindIndex(item => Math.Abs((double)(item.time - time)) <= 8d / (double)model.PixelsPerSecond)
                    : -1;
                if (eventIndex >= 0)
                {
                    model.EventIndex = eventIndex;
                    model.Selection.Clear();
                }
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Add Key"), false, controller.SetKeyAtCursor);
                menu.AddItem(new GUIContent("Copy Keys"), false, controller.CopySelectedKeys);
                menu.AddItem(new GUIContent("Paste Keys"), false, controller.PasteKeys);
                menu.AddItem(new GUIContent("Delete Keys"), false, controller.DeleteSelectedKeys);
                menu.AddItem(new GUIContent("Add Animation Event"), false, controller.AddEvent);
                if (eventIndex >= 0) menu.AddItem(new GUIContent("Delete Animation Event"), false, controller.DeleteEvent);
                menu.AddItem(new GUIContent("Frame Selected"), false, () => FrameSelection(activeClip, GUIUtility.currentViewWidth));
                menu.AddItem(new GUIContent("Frame All"), false, () => FrameAll(activeClip, GUIUtility.currentViewWidth));
                menu.ShowAsContext();
            };
            _trackCanvas.addKeyRequested += (row, time) =>
            {
                if (model.Clip is not { } activeClip || (uint)row >= (uint)activeClip.bindings.Count) return;
                model.BindingIndex = row; model.CurveBindings.Clear(); model.CurveBindings.Add(row);
                controller.SetTime(time); controller.SetKeyAtCursor();
            };
            _trackCanvas.addMarkerRequested += time => { controller.SetTime(time); controller.AddEvent(); };
            _trackCanvas.marqueeSelected += (ids, _) =>
            {
                model.Selection.Clear();
                foreach (var id in ids) if (id is AnimationKeySelection key) model.Selection.Add(key);
                Repaint();
            };
            _trackCanvas.keyPressed += evt =>
            {
                if (model.Clip is { } activeClip) HandleTrackKey(evt, activeClip, uiElementContainer.position.width);
            };
        }
        _trackCanvas.curves = model.Curves;
        _trackCanvas.startTime = model.StartTime;
        _trackCanvas.pixelsPerSecond = model.PixelsPerSecond;
        _trackCanvas.verticalScroll = model.VerticalScroll;
        _trackCanvas.playhead = model.Time;
        _trackCanvas.frameRate = AnimationTimelineEditing.Rate(clip);
        _trackCanvas.sidebarWidth = 0;
        var rows = clip.bindings.Select((binding, index) =>
        {
            var label = (binding.relativePath.Length == 0 ? model.Target?.name ?? "Root" : binding.relativePath) +
                        " / " + binding.componentType.Split('.').Last();
            var row = new TrackEditorTrack(index, label)
            {
                sample = time => binding.kind == AnimationBindingKind.Numeric ? binding.curve.Evaluate(time) : binding.curve.EvaluateDiscrete(time),
                selected = model.CurveBindings.Contains(index),
                curveVisible = model.CurveBindings.Count == 0
                    ? index == model.BindingIndex : model.CurveBindings.Contains(index),
                color = (index % 3) switch
                {
                    0 => new UIColor(230, 118, 104),
                    1 => new UIColor(105, 213, 132),
                    _ => new UIColor(98, 158, 234)
                }
            };
            row.keys.AddRange(binding.curve.keys.Select(key => new TrackEditorKey(
                new AnimationKeySelection(index, key.time), key.time, key.value,
                model.Selection.Contains(new AnimationKeySelection(index, key.time)),
                key.inTangent, key.outTangent)));
            return row;
        }).ToArray();
        var markers = clip.events.Select((animationEvent, index) =>
            new TrackEditorMarker(animationEvent, animationEvent.time, index == model.EventIndex)).ToArray();
        _trackCanvas.SetContent((float)area.width, (float)area.height, rows, markers);
        uiElementContainer.SetRootVisualElement(_trackCanvas);
        uiElementContainer.position = area;
        uiElementContainer.contentSize = new Vector2(area.width, area.height);
        uiElementContainer.visible = true;
        uiElementContainer.interactable = true;
    }

    private void HandleTrackKey(KeyDownEvent evt, AnimationClip clip, Fix64 width)
    {
        if ((evt.ctrlKey || evt.commandKey) && evt.key == "Z") { if (evt.shiftKey) Undo.PerformRedo(); else Undo.PerformUndo(); }
        else if ((evt.ctrlKey || evt.commandKey) && evt.key == "Y") Undo.PerformRedo();
        else if ((evt.ctrlKey || evt.commandKey) && evt.key == "C") controller.CopySelectedKeys();
        else if ((evt.ctrlKey || evt.commandKey) && evt.key == "V") controller.PasteKeys();
        else if ((evt.ctrlKey || evt.commandKey) && evt.key == "A")
        {
            model.Selection.Clear();
            for (var index = 0; index < clip.bindings.Count; index++)
                foreach (var key in clip.bindings[index].curve.keys) model.Selection.Add(new(index, key.time));
        }
        else if (evt.key is "Delete" or "Backspace")
        { if (model.EventIndex >= 0) controller.DeleteEvent(); else controller.DeleteSelectedKeys(); }
        else if (evt.key == "Space") controller.TogglePreview();
        else if (evt.key == "Comma") { if (evt.altKey) controller.SeekKey(-1); else controller.Step(-1); }
        else if (evt.key == "Period") { if (evt.altKey) controller.SeekKey(1); else controller.Step(1); }
        else if (evt.key is "Left" or "LeftArrow") { if (evt.altKey) controller.SeekKey(-1); else controller.Step(-1); }
        else if (evt.key is "Right" or "RightArrow") { if (evt.altKey) controller.SeekKey(1); else controller.Step(1); }
        else if (evt.key == "Home") controller.SetTime(0);
        else if (evt.key == "End") controller.SetTime(clip.length);
        else if (evt.key == "K") controller.SetKeyAtCursor();
        else if (evt.key == "F") FrameSelection(clip, width);
        else if (evt.key == "A") FrameAll(clip, width);
        else if (evt.key == "Escape") controller.StopPreview();
        else return;
        Repaint();
    }
}
