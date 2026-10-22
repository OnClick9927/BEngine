using BEngine.UIElements;

namespace BEngine.Editor;

[EditorWindowIcon("Timeline.png")]
public sealed class TimelineWindow : UIElementsEditorWindow<TimelineWindowModel, TimelineWindowController>
{
    private TrackEditorCanvas? _canvas;
    private bool _wired;

    public TimelineWindow() { titleContent = new GUIContent("Timeline"); minSize = new Vector2(780, 350); }
    protected override TimelineWindowModel CreateWindowModel(EditorWindowMvcContext context) => new(context.WindowId);
    protected override TimelineWindowController CreateWindowController(EditorWindowMvcContext context,
        TimelineWindowModel model) => new(context, model);

    [MenuItem("Window/Animation/Timeline", false, 232)]
    public static void Open() => GetWindow<TimelineWindow>("Timeline").Show();

    public static void Open(TimelineAsset asset)
    {
        var window = GetWindow<TimelineWindow>("Timeline");
        window.controller.Select(asset);
        window.Show();
    }

    protected override void OnEnable()
    {
        OnSelectionChange();
        EditorApplication.update += UpdateTimeline;
        EditorApplication.playModeStateChanged += OnPlayMode;
        Undo.undoRedoPerformed += OnUndo;
    }
    protected override void OnDisable()
    {
        controller.StopPreview(); uiElementContainer.visible = false;
        EditorApplication.update -= UpdateTimeline;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        Undo.undoRedoPerformed -= OnUndo;
    }
    protected override void OnLostFocus() => controller.EndClipDrag();
    private void UpdateTimeline()
    {
        controller.Update();
        if (EditorApplication.isPlaying && controller.ObserveRuntime()) Repaint();
    }
    private void OnPlayMode(PlayModeStateChange state)
    {
        // A runtime Director is owned by SceneRuntime.  The editor must keep
        // observing it instead of stopping it when Play Mode starts.
        if (state is PlayModeStateChange.ExitingEditMode or PlayModeStateChange.ExitingPlayMode) controller.StopPreview();
    }
    private void OnUndo()
    {
        controller.EndClipDrag();
        var previousTrack = model.SelectedTrack;
        var selectedIndex = previousTrack is not null && model.SelectedClip is { } selected
            ? previousTrack.clips.IndexOf(selected) : -1;
        var track = model.Asset?.tracks.FirstOrDefault(item => item.id == previousTrack?.id);
        model.SelectedTrack = track;
        model.SelectedClip = track is not null && (uint)selectedIndex < (uint)track.clips.Count
            ? track.clips[selectedIndex] : null;
        Repaint();
    }

    protected override void OnSelectionChange()
    {
        if (model.Locked) return;
        if (Selection.activeObject is TimelineAsset asset)
        {
            controller.StopPreview();
            model.Director = null;
            if (!ReferenceEquals(model.Asset, asset)) controller.Select(asset);
        }
        else if (Selection.activeGameObject?.GetComponent<PlayableDirector>() is { } director)
        {
            if (model.Director is { } previous && !ReferenceEquals(previous, director)) controller.StopPreview();
            if (!ReferenceEquals(model.Asset, director.playableAsset)) controller.Select(director.playableAsset);
            model.Director = director;
        }
    }

    protected override void OnGUI()
    {
        using var readOnly = new EditorGUI.DisabledScope(!EditorAssetWritePolicy.CanWrite);
        var width = GUIUtility.currentViewWidth;
        var height = GUIUtility.currentViewHeight;
        DrawToolbar(width);
        if (model.Asset is not { isDestroyed: false } asset)
        {
            uiElementContainer.visible = false;
            GUI.Label(new Rect(12, 73, width - 24, 40),
                "Select a Timeline asset or create one to start authoring.");
            return;
        }
        var inspectorWidth = model.ShowInspector ? Fix64.Min(260, width / 3) : Fix64.Zero;
        var area = new Rect(0, 61, width - inspectorWidth, Fix64.Max(100, height - 61));
        ConfigureCanvas(area, asset);
        if (model.ShowInspector) DrawInspector(new Rect(area.xMax, 61, inspectorWidth, area.height), asset);
    }

    private void DrawToolbar(Fix64 width)
    {
        GUI.DrawRect(new Rect(0, 0, width, 60), new Color((Fix64).16, (Fix64).17, (Fix64).18));
        if (GUI.Button(new Rect(5, 4, 28, 22), new GUIContent(string.Empty,
                EditorBuiltinIcons.Toolbar.New, "New Timeline")))
            controller.Select(new TimelineAsset { name = "New Timeline" });
        using (new EditorGUI.DisabledScope(model.Asset is null))
            if (GUI.Button(new Rect(36, 4, 28, 22), new GUIContent(string.Empty,
                    EditorBuiltinIcons.Toolbar.Save, "Save Timeline"))) controller.Save();
        if (GUI.Button(new Rect(68, 4, 94, 22), new GUIContent("Add Track",
                EditorBuiltinIcons.Toolbar.Add, "Add a Timeline track"))) ShowTrackMenu();
        var selected = EditorGUI.ObjectField(new Rect(168, 4, Fix64.Max(80, width - 371), 22), model.Asset, false);
        if (!ReferenceEquals(selected, model.Asset)) controller.Select(selected);
        model.Locked = GUI.Toggle(new Rect(width - 195, 4, 54, 22), model.Locked, "Lock", EditorStyles.toolbarButton);
        model.ShowInspector = GUI.Toggle(new Rect(width - 137, 4, 132, 22), model.ShowInspector,
            "Clip Inspector", EditorStyles.toolbarButton);

        var canPreview = model.Director is { isDestroyed: false } boundDirector &&
            ReferenceEquals(boundDirector.playableAsset, model.Asset);
        using (new EditorGUI.DisabledScope(!canPreview))
        {
            if (GUI.Toggle(new Rect(5, 33, 70, 22), model.Previewing,
                    "Preview", EditorStyles.toolbarButton) != model.Previewing)
                controller.SetPreview(!model.Previewing);
            if (GUI.Button(new Rect(79, 33, 28, 22), new GUIContent("|<", tooltip: "Go to start")))
                controller.SetTime(0);
            if (GUI.Button(new Rect(110, 33, 28, 22), new GUIContent("<", tooltip: "Previous frame")))
                controller.Step(-1);
            if (GUI.Button(new Rect(141, 33, 28, 22), new GUIContent(string.Empty,
                    model.Playing ? EditorBuiltinIcons.Toolbar.Pause : EditorBuiltinIcons.Toolbar.Play,
                    model.Playing ? "Pause Timeline" : "Play Timeline"))) controller.TogglePlay();
            if (GUI.Button(new Rect(172, 33, 28, 22), new GUIContent(string.Empty,
                    EditorBuiltinIcons.Toolbar.Step, "Next frame"))) controller.Step(1);
            if (GUI.Button(new Rect(203, 33, 28, 22), new GUIContent(">|", tooltip: "Go to end")))
                controller.SetTime(model.Asset?.duration ?? 0);
        }
        GUI.Label(new Rect(240, 36, 40, 18), "Frame", EditorStyles.miniLabel);
        var frame = Mathf.RoundToInt(model.Time * 30);
        var nextFrame = EditorGUI.IntField(new Rect(281, 33, 54, 22), frame);
        if (nextFrame != frame) controller.SetTime((Fix64)Math.Max(0, nextFrame) / 30);
        model.Snap = GUI.Toggle(new Rect(341, 33, 60, 22), model.Snap, "Snap", EditorStyles.toolbarButton);
        if (GUI.Button(new Rect(405, 33, 77, 22), "Frame All"))
            controller.FrameAll(width - (model.ShowInspector ? Fix64.Min(260, width / 3) : 0));
        GUI.Label(new Rect(width - 96, 36, 91, 18),
            $"{(double)(model.Asset?.duration ?? Fix64.Zero):0.##} s", EditorStyles.miniLabel);
        if (EditorApplication.isPlaying && model.RuntimeLabel.Length > 0)
            GUI.Label(new Rect(width - 300, 36, 200, 18), model.RuntimeLabel, EditorStyles.miniLabel);
    }

    private void ShowTrackMenu()
    {
        var menu = new GenericMenu();
        foreach (var type in Enum.GetValues<TimelineTrackType>())
        {
            var selected = type;
            menu.AddItem(new GUIContent(type.ToString()), false, () => controller.AddTrack(selected));
        }
        menu.ShowAsContext();
    }

    private void DrawInspector(Rect rect, TimelineAsset asset)
    {
        GUI.DrawRect(rect, new Color((Fix64).18, (Fix64).19, (Fix64).20));
        var viewport = new Rect(rect.x, rect.y, rect.width, rect.height);
        model.InspectorScroll = GUI.BeginScrollView(viewport, model.InspectorScroll,
            new Rect(0, 0, rect.width - 14, 800));
        try
        {
        rect = new Rect(0, 0, rect.width - 14, 800);
        var x = rect.x + 8; var width = rect.width - 16;
        GUI.Label(new Rect(x, rect.y + 6, width, 22), "Timeline", EditorStyles.boldLabel);
        GUI.Label(new Rect(x, rect.y + 31, width, 18), "Director", EditorStyles.miniLabel);
        var director = EditorGUI.ObjectField(new Rect(x, rect.y + 51, width, 22),
            model.Director, true);
        if (!ReferenceEquals(director, model.Director))
        {
            controller.StopPreview();
            model.Director = director;
            if (director is not null && !ReferenceEquals(director.playableAsset, asset))
            {
                Undo.IncrementCurrentGroup(); Undo.RecordObject(director, "Assign Timeline Asset");
                director.playableAsset = asset; EditorUtility.SetDirty(director);
            }
            controller.SetTime(model.Time);
        }
        if (model.SelectedTrack is not { } track || !asset.tracks.Contains(track))
        {
            GUI.Label(new Rect(x, rect.y + 90, width, 26),
                $"{asset.tracks.Count} tracks  |  {(double)asset.duration:0.##} s", EditorStyles.miniLabel);
            return;
        }
        GUI.Label(new Rect(x, rect.y + 87, width, 21), "Track", EditorStyles.boldLabel);
        var name = EditorGUI.TextField(new Rect(x, rect.y + 109, width, 22), track.name);
        if (name != track.name && !string.IsNullOrWhiteSpace(name))
            AssetEditing.Edit(asset, "Rename Timeline Track", () => track.name = name);
        GUI.Label(new Rect(x, rect.y + 136, width, 19), track.type.ToString(), EditorStyles.miniLabel);
        var muted = GUI.Toggle(new Rect(x, rect.y + 159, 90, 22), track.muted, "Mute");
        var locked = GUI.Toggle(new Rect(x + 96, rect.y + 159, 90, 22), track.locked, "Lock");
        if (muted != track.muted || locked != track.locked)
            AssetEditing.Edit(asset, "Timeline Track Settings", () => { track.muted = muted; track.locked = locked; });
        if (model.Director is { } selectedDirector && track.type != TimelineTrackType.Signal)
        {
            GUI.Label(new Rect(x, rect.y + 187, width, 18), "Binding", EditorStyles.miniLabel);
            var binding = EditorGUI.ObjectField(new Rect(x, rect.y + 207, width, 22),
                selectedDirector.GetGenericBinding(track.id), true);
            if (!ReferenceEquals(binding, selectedDirector.GetGenericBinding(track.id)))
            {
                Undo.IncrementCurrentGroup(); Undo.RecordObject(selectedDirector, "Bind Timeline Track");
                selectedDirector.SetGenericBinding(track.id, binding); EditorUtility.SetDirty(selectedDirector);
            }
        }
        var trackActionsY = model.Director is null || track.type == TimelineTrackType.Signal ? 216 : 236;
        if (track.type == TimelineTrackType.Control)
        {
            var activation = GUI.Toggle(new Rect(x, rect.y + trackActionsY, width, 22), track.controlActivation, "Control Activation");
            if (activation != track.controlActivation)
                AssetEditing.Edit(asset, "Timeline Control Settings", () => track.controlActivation = activation);
            trackActionsY += 26;
        }
        if (GUI.Button(new Rect(x, rect.y + trackActionsY, width, 22), "Add Clip")) controller.AddClip(track, model.Time);
        if (GUI.Button(new Rect(x, rect.y + trackActionsY + 25, width, 22), "Remove Track")) controller.DeleteSelectedTrack();
        if (model.SelectedClip is not { } clip || !track.clips.Contains(clip)) return;
        var y = rect.y + trackActionsY + 64;
        GUI.Label(new Rect(x, y, width, 21), "Clip", EditorStyles.boldLabel);
        var clipName = EditorGUI.TextField(new Rect(x, y + 24, width, 22), clip.name);
        var start = TimelineFloatField(new Rect(x, y + 50, width, 22), "Start", (float)clip.start);
        var duration = TimelineFloatField(new Rect(x, y + 76, width, 22), "Duration", (float)clip.duration);
        if (clipName != clip.name || start != (float)clip.start || duration != (float)clip.duration)
            controller.EditClip(clip, () =>
            {
                clip.name = clipName; clip.start = Fix64.Max(0, (Fix64)start);
                clip.duration = Fix64.Max(0, (Fix64)duration);
            });
        var extra = y + 105;
        if (track.type is TimelineTrackType.Animation or TimelineTrackType.Camera)
        {
            var (effectiveIn, effectiveOut) = PlayableDirector.BlendDurations(clip, track.clips);
            var easeIn = TimelineFloatField(new Rect(x, extra, width, 22), "Blend In", (float)effectiveIn);
            var easeOut = TimelineFloatField(new Rect(x, extra + 26, width, 22), "Blend Out", (float)effectiveOut);
            if (easeIn != (float)effectiveIn || easeOut != (float)effectiveOut)
                controller.EditClip(clip, () =>
                {
                    clip.easeInDuration = Fix64.Clamp((Fix64)easeIn, 0, clip.duration);
                    clip.easeOutDuration = Fix64.Clamp((Fix64)easeOut, 0, clip.duration);
                });
            extra += 56;
        }
        if (track.type == TimelineTrackType.Animation)
        {
            GUI.Label(new Rect(x, extra, width, 18), "Animation", EditorStyles.miniLabel);
            var motion = EditorGUI.ObjectField(new Rect(x, extra + 20, width, 22), clip.clip, false);
            if (!ReferenceEquals(motion, clip.clip)) controller.EditClip(clip, () => clip.clip = motion);
        }
        else if (track.type is TimelineTrackType.Audio or TimelineTrackType.Video)
        {
            GUI.Label(new Rect(x, extra, width, 18), "Media", EditorStyles.miniLabel);
            var media = EditorGUI.ObjectField(new Rect(x, extra + 20, width, 22), clip.mediaAsset, false);
            if (!ReferenceEquals(media, clip.mediaAsset)) controller.EditClip(clip, () => clip.mediaAsset = media);
        }
        else if (track.type == TimelineTrackType.Signal)
        {
            var signal = EditorGUI.TextField(new Rect(x, extra, width, 22), "Signal", clip.signalName);
            if (signal != clip.signalName) controller.EditClip(clip, () => clip.signalName = signal);
        }
        else if (track.type == TimelineTrackType.Control)
        {
            GUI.Label(new Rect(x, extra, width, 18), "Nested Timeline (optional)", EditorStyles.miniLabel);
            var nested = EditorGUI.ObjectField(new Rect(x, extra + 20, width, 22), clip.nestedTimeline, false);
            if (!ReferenceEquals(nested, clip.nestedTimeline)) controller.EditClip(clip, () => clip.nestedTimeline = nested);
        }
        else if (track.type == TimelineTrackType.Camera && model.Director is { } cameraDirector)
        {
            GUI.Label(new Rect(x, extra, width, 18), "Shot Camera", EditorStyles.miniLabel);
            var current = cameraDirector.GetGenericBinding(clip.cameraBinding)?.GetComponent<Camera2D>();
            var shot = EditorGUI.ObjectField(new Rect(x, extra + 20, width, 22), current, true);
            if (!ReferenceEquals(shot, current)) controller.SetCameraSource(clip, shot);
        }
        var settingsY = extra + 48;
        if (track.type == TimelineTrackType.Control)
        {
            var loop = GUI.Toggle(new Rect(x, settingsY, width, 22), clip.loopNested, "Loop Nested Timeline");
            if (loop != clip.loopNested) controller.EditClip(clip, () => clip.loopNested = loop);
            settingsY += 26;
        }
        if (track.type is TimelineTrackType.Animation or TimelineTrackType.Audio or TimelineTrackType.Video or TimelineTrackType.Control)
        {
            var clipIn = TimelineFloatField(new Rect(x, settingsY, width, 22), "Clip In", (float)clip.clipIn);
            var scale = TimelineFloatField(new Rect(x, settingsY + 26, width, 22), "Speed", (float)clip.timeScale);
            if (clipIn != (float)clip.clipIn || scale != (float)clip.timeScale)
                controller.EditClip(clip, () =>
                { clip.clipIn = Fix64.Max(0, (Fix64)clipIn); clip.timeScale = Fix64.Max((Fix64).01, (Fix64)scale); });
        }
        if (GUI.Button(new Rect(x, settingsY + 58, width, 22), "Delete Clip")) controller.DeleteSelectedClip();
        }
        finally { GUI.EndScrollView(); }
    }

    private static float TimelineFloatField(Rect area, string label, float value)
    {
        EditorGUI.BeginChangeCheck();
        var result = EditorGUI.FloatField(area, label, value);
        return EditorGUI.EndChangeCheck() ? result : value;
    }

    private static float TimelineFloatField(Rect area, float value)
    {
        EditorGUI.BeginChangeCheck();
        var result = EditorGUI.FloatField(area, value);
        return EditorGUI.EndChangeCheck() ? result : value;
    }

    private void ConfigureCanvas(Rect area, TimelineAsset asset)
    {
        _canvas ??= new TrackEditorCanvas();
        if (!_wired)
        {
            _wired = true;
            _canvas.timeChanged += controller.SetTime;
            _canvas.viewChanged += (start, pixels, scroll) =>
            { model.StartTime = start; model.PixelsPerSecond = pixels; model.VerticalScroll = scroll; Repaint(); };
            _canvas.trackSelected += (id, _) => { if (id is TimelineTrack track) controller.SelectTrack(track); };
            _canvas.clipSelected += (id, _) =>
            {
                if (id is TimelineClip clip && model.Asset is { } current)
                {
                    var track = current.tracks.FirstOrDefault(item => item.clips.Contains(clip));
                    if (track is not null) controller.SelectTrack(track);
                    model.SelectedClip = clip; Repaint();
                }
            };
            _canvas.clipDragStarted += controller.BeginClipDrag;
            _canvas.clipDragged += (id, delta) => controller.MoveClip(id, delta);
            _canvas.clipStartTrimmed += (id, delta) => controller.TrimClipStart(id, delta);
            _canvas.clipTrimmed += (id, delta) => controller.TrimClip(id, delta);
            _canvas.clipBlendInDragged += (id, duration) => controller.SetClipBlend(id, duration, true);
            _canvas.clipBlendOutDragged += (id, duration) => controller.SetClipBlend(id, duration, false);
            _canvas.gestureEnded += controller.EndClipDrag;
            _canvas.trackHeaderAction += (id, action) =>
            {
                if (id is TimelineTrack track) controller.ToggleTrack(track, action);
            };
            _canvas.addClipRequested += (id, time) => controller.AddClip(id as TimelineTrack, time);
            _canvas.addKeyRequested += (row, time) =>
            {
                if (model.Asset is { } current && (uint)row < (uint)current.tracks.Count)
                    controller.AddClip(current.tracks[row], time);
            };
            _canvas.addMarkerRequested += time => controller.AddSignal(time);
            _canvas.contextRequested += point =>
            {
                var time = model.StartTime + (point.x - (Fix64)_canvas.sidebarWidth - 10) / model.PixelsPerSecond;
                controller.SetTime(time);
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Add Clip"), false,
                    () => controller.AddClip(model.SelectedTrack, model.Time));
                menu.AddItem(new GUIContent("Duplicate Clip"), false, controller.DuplicateSelectedClip);
                menu.AddItem(new GUIContent("Split Clip"), false, controller.SplitSelectedClip);
                menu.AddItem(new GUIContent("Delete Clip"), false, controller.DeleteSelectedClip);
                menu.AddItem(new GUIContent("Remove Track"), false, controller.DeleteSelectedTrack);
                menu.AddItem(new GUIContent("Move Track Up"), false, () => controller.MoveSelectedTrack(-1));
                menu.AddItem(new GUIContent("Move Track Down"), false, () => controller.MoveSelectedTrack(1));
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Preview"), model.Previewing,
                    () => controller.SetPreview(!model.Previewing));
                menu.AddItem(new GUIContent("Frame All"), false,
                    () => controller.FrameAll(uiElementContainer.position.width));
                menu.ShowAsContext();
            };
            _canvas.markerSelected += (id, _) =>
            {
                if (id is TimelineClip clip && model.Asset is { } current)
                {
                    controller.SelectTrack(current.tracks.FirstOrDefault(item => item.clips.Contains(clip)));
                    model.SelectedClip = clip; Repaint();
                }
            };
            _canvas.keyPressed += evt =>
            {
                if (evt.key is "Delete" or "Backspace") controller.DeleteSelectedClip();
                else if (evt.key == "Space") controller.TogglePlay();
                else if (evt.key is "Left" or "LeftArrow") controller.Step(-1);
                else if (evt.key is "Right" or "RightArrow") controller.Step(1);
                else if (evt.key is "S" or "s") controller.SplitSelectedClip();
                else if (evt.ctrlKey && evt.key is ("D" or "d")) controller.DuplicateSelectedClip();
                Repaint();
            };
        }
        _canvas.startTime = model.StartTime;
        _canvas.pixelsPerSecond = model.PixelsPerSecond;
        _canvas.verticalScroll = model.VerticalScroll;
        _canvas.playhead = model.Time;
        _canvas.frameRate = 30;
        _canvas.sidebarWidth = (float)SidebarWidth(area.width);
        _canvas.sidebarHeader = "Tracks";
        _canvas.sidebarFooter = "Timeline";
        _canvas.markerLabel = "Signals";
        var rows = asset.tracks.Select(track =>
        {
            var row = new TrackEditorTrack(track, track.name)
            {
                color = TrackColor(track.type), selected = ReferenceEquals(model.SelectedTrack, track),
                muted = track.muted, locked = track.locked
            };
            foreach (var clip in track.clips)
            {
                var (blendIn, blendOut) = track.type is TimelineTrackType.Animation or TimelineTrackType.Camera
                    ? PlayableDirector.BlendDurations(clip, track.clips)
                    : (Fix64.Zero, Fix64.Zero);
                row.clips.Add(new TrackEditorClip(clip, string.IsNullOrWhiteSpace(clip.name) ? track.name : clip.name,
                    clip.start, clip.duration, row.color, ReferenceEquals(model.SelectedClip, clip))
                {
                    blendIn = blendIn, blendOut = blendOut,
                    blendEditable = track.type is TimelineTrackType.Animation or TimelineTrackType.Camera && !track.locked
                });
            }
            return row;
        }).ToArray();
        var markers = asset.tracks.Where(track => track.type == TimelineTrackType.Signal)
            .SelectMany(track => track.clips)
            .Select(clip => new TrackEditorMarker(clip, clip.start, ReferenceEquals(model.SelectedClip, clip))).ToArray();
        _canvas.SetContent((float)area.width, (float)area.height, rows, markers);
        uiElementContainer.SetRootVisualElement(_canvas);
        uiElementContainer.position = area;
        uiElementContainer.contentSize = new Vector2(area.width, area.height);
        uiElementContainer.visible = true;
        uiElementContainer.interactable = EditorAssetWritePolicy.CanWrite;
    }

    private static UIColor TrackColor(TimelineTrackType type) => type switch
    {
        TimelineTrackType.Animation => new UIColor(96, 168, 222),
        TimelineTrackType.Activation => new UIColor(125, 196, 132),
        TimelineTrackType.Signal => new UIColor(225, 173, 82),
        TimelineTrackType.Audio => new UIColor(193, 125, 212),
        TimelineTrackType.Video => new UIColor(222, 112, 107),
        TimelineTrackType.Control => new UIColor(100, 185, 179),
        TimelineTrackType.Camera => new UIColor(135, 142, 228),
        _ => new UIColor(150, 160, 168)
    };
    internal static Fix64 SidebarWidth(Fix64 canvasWidth) =>
        Fix64.Min(Fix64.Max(1, canvasWidth - 40),
            Fix64.Clamp(canvasWidth * (Fix64).36, 140, 220));
}

public sealed class TimelineWindowModel(string windowId) : EditorWindowModel(windowId)
{
    public TimelineAsset? Asset { get; internal set; }
    public PlayableDirector? Director { get; internal set; }
    public TimelineTrack? SelectedTrack { get; internal set; }
    public TimelineClip? SelectedClip { get; internal set; }
    public Fix64 Time { get; internal set; }
    public Fix64 StartTime { get; internal set; }
    public Fix64 PixelsPerSecond { get; internal set; } = 120;
    public Fix64 VerticalScroll { get; internal set; }
    public bool Playing { get; internal set; }
    public bool Previewing { get; internal set; } = true;
    public bool Snap { get; internal set; } = true;
    public bool ShowInspector { get; internal set; } = true;
    public string RuntimeLabel { get; internal set; } = string.Empty;
    public Vector2 InspectorScroll { get; internal set; }
    internal bool Locked;
    internal string Message = string.Empty;
}

public sealed class TimelineWindowController : EditorWindowController<TimelineWindow, TimelineWindowModel>
{
    private double _lastTime;
    private TimelineClip? _dragClip;
    private Fix64 _dragStart;
    private Fix64 _dragDuration;
    private Fix64 _dragClipIn;
    private bool _dragRecorded;
    private string _lastRuntimeLabel = string.Empty;
    private readonly TimelineEditorPreview _preview;
    public TimelineWindowController(EditorWindowMvcContext context, TimelineWindowModel model) : base(context, model)
    { _preview = new(() => { Model.Playing = false; Repaint(); }); }

    internal bool ObserveRuntime()
    {
        if (!EditorApplication.isPlaying || Model.Director is not { isDestroyed: false } director ||
            !ReferenceEquals(director.playableAsset, Model.Asset))
        {
            if (Model.RuntimeLabel.Length == 0) return false;
            Model.RuntimeLabel = string.Empty; _lastRuntimeLabel = string.Empty; return true;
        }
        var active = Model.Asset?.tracks
            .SelectMany(track => track.clips.Where(clip => clip.duration > 0 &&
                director.time >= clip.start && director.time <= clip.end)
                .Select(clip => $"{track.name}/{clip.name}"))
            .Take(2).ToArray() ?? [];
        var activeLabel = active.Length == 0 ? string.Empty : "  " + string.Join(", ", active);
        var label = $"Runtime  {director.time:0.###} s" + (director.isPlaying ? "  Playing" : "  Paused") + activeLabel;
        if (label == _lastRuntimeLabel) return false;
        _lastRuntimeLabel = label; Model.RuntimeLabel = label; Model.Time = director.time; return true;
    }

    public void Select(TimelineAsset? asset)
    {
        if (!ReferenceEquals(Model.Asset, asset)) StopPreview();
        if (Model.Director is { } director && !ReferenceEquals(director.playableAsset, asset))
            Model.Director = null;
        Model.Asset = asset; Model.SelectedTrack = null; Model.SelectedClip = null; Model.Time = 0; Model.StartTime = 0;
        Model.VerticalScroll = 0; Model.Message = string.Empty; Repaint();
    }

    public void Save()
    {
        if (Model.Asset is not { } asset) return;
        try
        {
            var path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrWhiteSpace(path))
                path = AssetDatabase.GenerateUniqueAssetPath($"{ProjectWindowUtil.GetActiveFolderPath() ?? "Assets"}/{asset.name}.timeline.yaml");
            if (!path.Replace('\\', '/').StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Timeline assets must be saved inside Assets.");
            var fullPath = EditorServices.GetRequiredService<PathUtility>().Combine(EditorApplication.projectPath, path);
            asset.Save(fullPath); AssetDatabase.ImportAsset(path); EditorUtility.ClearDirty(asset); Model.Message = "Saved " + path;
        }
        catch (Exception exception) { Model.Message = exception.Message; }
        Repaint();
    }

    public void AddTrack(TimelineTrackType type)
    {
        if (Model.Asset is not { } asset) return;
        AssetEditing.Edit(asset, "Add Timeline Track", () => asset.tracks.Add(new TimelineTrack
        { name = type + " Track", type = type }));
        Model.SelectedTrack = asset.tracks.Last();
        Repaint();
    }

    public void SelectTrack(TimelineTrack? track)
    {
        if (ReferenceEquals(Model.SelectedTrack, track)) return;
        Model.SelectedTrack = track;
        Model.SelectedClip = null;
        Repaint();
    }

    public void DeleteSelectedTrack()
    {
        if (Model.Asset is not { } asset || Model.SelectedTrack is not { } track ||
            !asset.tracks.Contains(track) || track.locked) return;
        AssetEditing.Edit(asset, "Remove Timeline Track", () => asset.tracks.Remove(track));
        Model.SelectedTrack = null; Model.SelectedClip = null; Repaint();
    }

    public void MoveSelectedTrack(int direction)
    {
        if (Model.Asset is not { } asset || Model.SelectedTrack is not { } track || track.locked) return;
        var index = asset.tracks.IndexOf(track);
        var destination = index + Math.Sign(direction);
        if ((uint)index >= (uint)asset.tracks.Count || (uint)destination >= (uint)asset.tracks.Count) return;
        AssetEditing.Edit(asset, "Move Timeline Track", () =>
        {
            asset.tracks.RemoveAt(index);
            asset.tracks.Insert(destination, track);
        });
        Repaint();
    }

    public void AddClip(TimelineTrack? track, Fix64 time)
    {
        if (Model.Asset is not { } asset) return;
        track ??= Model.SelectedTrack ?? asset.tracks.FirstOrDefault(item => !item.locked);
        if (track is null || !asset.tracks.Contains(track) || track.locked) return;
        if (track.type == TimelineTrackType.Signal) { AddSignal(time, track); return; }
        var selected = Selection.activeObject;
        var duration = selected is AnimationClip motion && motion.length > 0 ? motion.length : (Fix64)1;
        var clip = new TimelineClip
        {
            name = selected is BAsset source && !ReferenceEquals(source, asset) ? source.name : track.type + " Clip",
            start = Snap(time), duration = duration,
            clip = track.type == TimelineTrackType.Animation ? selected as AnimationClip : null,
            mediaAsset = track.type is TimelineTrackType.Audio or TimelineTrackType.Video &&
                !ReferenceEquals(selected, asset) ? selected as BAsset : null,
            nestedTimeline = track.type == TimelineTrackType.Control && !ReferenceEquals(selected, asset) ? selected as TimelineAsset : null
        };
        AssetEditing.Edit(asset, "Add Timeline Clip", () => track.clips.Add(clip));
        Model.SelectedTrack = track; Model.SelectedClip = clip; Repaint();
    }

    public void AddAnimationClip(Fix64 time)
    {
        if (Model.Asset is not { } asset) return;
        var track = asset.tracks.FirstOrDefault(item => item.type == TimelineTrackType.Animation && !item.locked);
        if (track is null) { AddTrack(TimelineTrackType.Animation); track = asset.tracks.Last(); }
        AddClip(track, time);
    }

    public void AddSignal(Fix64 time, TimelineTrack? preferredTrack = null)
    {
        if (Model.Asset is not { } asset) return;
        var track = preferredTrack ?? asset.tracks.FirstOrDefault(item => item.type == TimelineTrackType.Signal && !item.locked);
        if (track is null) { AddTrack(TimelineTrackType.Signal); track = asset.tracks.Last(); }
        var clip = new TimelineClip { name = "Signal", start = Snap(time), duration = 0, signalName = "Signal" };
        AssetEditing.Edit(asset, "Add Timeline Signal", () => track.clips.Add(clip));
        Model.SelectedTrack = track; Model.SelectedClip = clip; Repaint();
    }

    public void EditClip(TimelineClip clip, Action change)
    {
        if (Model.Asset is not { } asset ||
            !asset.tracks.Any(track => track.clips.Contains(clip) && !track.locked)) return;
        AssetEditing.Edit(asset, "Edit Timeline Clip", () =>
        {
            change();
            ClampBlend(clip);
        });
        Repaint();
    }

    public void SetCameraSource(TimelineClip clip, Camera2D? camera)
    {
        EditorAssetWritePolicy.EnsureCanWrite("Binding a Timeline camera");
        if (Model.Asset is not { } asset || Model.Director is not { } director ||
            !asset.tracks.Any(track => track.type == TimelineTrackType.Camera && !track.locked && track.clips.Contains(clip))) return;
        Undo.IncrementCurrentGroup(); Undo.RecordObject(director, "Bind Timeline Shot Camera");
        director.SetGenericBinding(clip.cameraBinding, camera?.gameObject); EditorUtility.SetDirty(director); Repaint();
    }

    public void BeginClipDrag(object id)
    {
        EndClipDrag();
        if (id is not TimelineClip clip) return;
        _dragClip = clip; _dragStart = clip.start; _dragDuration = clip.duration; _dragClipIn = clip.clipIn;
    }

    public void EndClipDrag() { _dragClip = null; _dragRecorded = false; }

    public void MoveClip(object id, Fix64 delta)
    {
        if (id is not TimelineClip clip || Model.Asset is not { } asset ||
            !asset.tracks.Any(track => track.clips.Contains(clip) && !track.locked)) return;
        var next = Snap((_dragClip == clip ? _dragStart : clip.start) + delta);
        if (next == clip.start) return;
        RecordDrag(asset, "Move Timeline Clip"); clip.start = next; EditorUtility.SetDirty(asset); Repaint();
    }

    public void TrimClip(object id, Fix64 delta)
    {
        if (id is not TimelineClip clip || Model.Asset is not { } asset ||
            !asset.tracks.Any(track => track.clips.Contains(clip) && !track.locked)) return;
        var next = Fix64.Max((Fix64).05, (_dragClip == clip ? _dragDuration : clip.duration) + delta);
        if (next == clip.duration) return;
        RecordDrag(asset, "Trim Timeline Clip"); clip.duration = next;
        ClampBlend(clip); EditorUtility.SetDirty(asset); Repaint();
    }

    public void TrimClipStart(object id, Fix64 delta)
    {
        if (id is not TimelineClip clip || Model.Asset is not { } asset ||
            !asset.tracks.Any(track => track.clips.Contains(clip) && !track.locked) || clip.duration <= 0) return;
        var originalStart = _dragClip == clip ? _dragStart : clip.start;
        var originalDuration = _dragClip == clip ? _dragDuration : clip.duration;
        var originalIn = _dragClip == clip ? _dragClipIn : clip.clipIn;
        var nextStart = Fix64.Clamp(Snap(originalStart + delta), 0, originalStart + originalDuration - (Fix64).05);
        var nextIn = Fix64.Max(0, originalIn + (nextStart - originalStart) * clip.timeScale);
        if (nextStart == clip.start && nextIn == clip.clipIn) return;
        RecordDrag(asset, "Trim Timeline Clip Start");
        clip.start = nextStart; clip.duration = originalStart + originalDuration - nextStart; clip.clipIn = nextIn;
        ClampBlend(clip);
        EditorUtility.SetDirty(asset); Repaint();
    }

    public void SetClipBlend(object id, Fix64 duration, bool incoming)
    {
        if (id is not TimelineClip clip || Model.Asset is not { } asset ||
            !asset.tracks.Any(track => track.type is TimelineTrackType.Animation or TimelineTrackType.Camera &&
                track.clips.Contains(clip) && !track.locked)) return;
        var next = Fix64.Clamp(duration, 0, clip.duration);
        if (incoming ? clip.easeInDuration == next : clip.easeOutDuration == next) return;
        RecordDrag(asset, incoming ? "Blend Timeline Clip In" : "Blend Timeline Clip Out");
        if (incoming) clip.easeInDuration = next;
        else clip.easeOutDuration = next;
        EditorUtility.SetDirty(asset); Repaint();
    }

    public void ToggleTrack(TimelineTrack track, TrackEditorHeaderAction action)
    {
        if (Model.Asset is not { } asset || !asset.tracks.Contains(track)) return;
        AssetEditing.Edit(asset, action == TrackEditorHeaderAction.Mute ?
            "Mute Timeline Track" : "Lock Timeline Track", () =>
        {
            if (action == TrackEditorHeaderAction.Mute) track.muted = !track.muted;
            else track.locked = !track.locked;
        });
        Repaint();
    }

    public void SplitSelectedClip()
    {
        if (Model.Asset is not { } asset || Model.SelectedClip is not { } selected) return;
        var track = asset.tracks.FirstOrDefault(item => item.clips.Contains(selected));
        if (track is null || track.locked || selected.duration <= 0) return;
        var split = Snap(Model.Time);
        if (split <= selected.start + (Fix64).01 || split >= selected.end - (Fix64).01) return;
        var originalEnd = selected.end;
        var second = CopyClip(selected);
        second.start = split;
        second.duration = originalEnd - split;
        second.clipIn = selected.clipIn + (split - selected.start) * selected.timeScale;
        second.easeInDuration = 0;
        ClampBlend(second);
        AssetEditing.Edit(asset, "Split Timeline Clip", () =>
        {
            selected.duration = split - selected.start;
            selected.easeOutDuration = 0;
            ClampBlend(selected);
            track.clips.Insert(track.clips.IndexOf(selected) + 1, second);
            CopyCameraBinding(selected, second);
        });
        Model.SelectedClip = second; Repaint();
    }

    public void DuplicateSelectedClip()
    {
        if (Model.Asset is not { } asset || Model.SelectedClip is not { } selected) return;
        var track = asset.tracks.FirstOrDefault(item => item.clips.Contains(selected));
        if (track is null || track.locked) return;
        var duplicate = CopyClip(selected);
        duplicate.start = Snap(Fix64.Max(selected.end, track.clips.Max(item => item.end)));
        if (selected.duration <= 0) duplicate.start += (Fix64)1 / 30;
        AssetEditing.Edit(asset, "Duplicate Timeline Clip", () => { track.clips.Add(duplicate); CopyCameraBinding(selected, duplicate); });
        Model.SelectedClip = duplicate; Repaint();
    }

    private static TimelineClip CopyClip(TimelineClip source)
    {
        var copy = new TimelineClip
        {
            name = source.name, start = source.start, duration = source.duration,
            clipIn = source.clipIn, timeScale = source.timeScale,
            easeInDuration = source.easeInDuration, easeOutDuration = source.easeOutDuration,
            signalName = source.signalName, clipPath = source.clipPath,
            mediaPath = source.mediaPath, mediaType = source.mediaType,
            nestedTimelinePath = source.nestedTimelinePath, loopNested = source.loopNested
        };
        if (source.clip is { } motion) copy.clip = motion;
        if (source.mediaAsset is { } media) copy.mediaAsset = media;
        if (source.nestedTimeline is { } nested) copy.nestedTimeline = nested;
        return copy;
    }

    private void CopyCameraBinding(TimelineClip source, TimelineClip copy)
    {
        if (Model.Director is not { } director || director.GetGenericBinding(source.cameraBinding) is not { } target) return;
        Undo.RecordObject(director, "Copy Timeline Camera Binding");
        director.SetGenericBinding(copy.cameraBinding, target); EditorUtility.SetDirty(director);
    }

    private static void ClampBlend(TimelineClip clip)
    {
        var duration = Fix64.Max(0, clip.duration);
        clip.easeInDuration = Fix64.Clamp(clip.easeInDuration, 0, duration);
        clip.easeOutDuration = Fix64.Clamp(clip.easeOutDuration, 0, duration);
    }

    private void RecordDrag(TimelineAsset asset, string label)
    {
        EditorAssetWritePolicy.EnsureCanWrite(label);
        if (_dragRecorded) return;
        Undo.IncrementCurrentGroup(); Undo.RecordObject(asset, label); _dragRecorded = true;
    }

    public void DeleteSelectedClip()
    {
        if (Model.Asset is not { } asset || Model.SelectedClip is not { } selected) return;
        var track = asset.tracks.FirstOrDefault(item => item.clips.Contains(selected));
        if (track is null || track.locked) return;
        AssetEditing.Edit(asset, "Delete Timeline Clip", () => track.clips.Remove(selected)); Model.SelectedClip = null; Repaint();
    }

    public void SetTime(Fix64 time)
    {
        Model.Time = Fix64.Max(0, time);
        if (EditorAssetWritePolicy.CanWrite && Model.Previewing && Model.Director is { isDestroyed: false } director &&
            ReferenceEquals(director.playableAsset, Model.Asset))
        {
            _preview.Sample(director, Model.Time);
        }
        Repaint();
    }
    public void Step(int direction) => SetTime(Model.Time + (Fix64)direction / 30);
    public void SetPreview(bool preview)
    {
        if (Model.Previewing == preview) return;
        Model.Previewing = preview;
        if (preview) SetTime(Model.Time);
        else { StopPreview(); Repaint(); }
    }
    public void TogglePlay()
    {
        if (Model.Asset is null || Model.Director is not { isDestroyed: false } director ||
            !ReferenceEquals(director.playableAsset, Model.Asset)) return;
        if (Model.Asset.duration > 0 && Model.Time >= Model.Asset.duration) SetTime(0);
        Model.Playing = !Model.Playing;
        if (Model.Playing && !Model.Previewing) SetPreview(true);
        _lastTime = EditorApplication.timeSinceStartup; Repaint();
    }
    public void StopPreview()
    {
        Model.Playing = false;
        _preview.Dispose();
    }
    public void FrameAll(Fix64 width)
    {
        if (Model.Asset is not { } asset) return;
        Model.StartTime = 0;
        Model.PixelsPerSecond = Fix64.Clamp((width - TimelineWindow.SidebarWidth(width) - 20) /
            Fix64.Max((Fix64).5, asset.duration + (Fix64).25), 12, 1200);
        Repaint();
    }
    public void Update()
    {
        if (!Model.Playing || Model.Asset is not { } asset) return;
        var now = EditorApplication.timeSinceStartup;
        var time = Model.Time + (Fix64)Math.Clamp(now - _lastTime, 0, .1); _lastTime = now;
        if (asset.duration > 0 && time >= asset.duration)
        {
            if (Model.Director?.wrapLoop == true) time %= asset.duration;
            else { time = asset.duration; Model.Playing = false; }
        }
        SetTime(time);
    }
    private Fix64 Snap(Fix64 time) => Fix64.Max(0, Model.Snap ? Mathf.Round(time * 30) / 30 : time);
}
