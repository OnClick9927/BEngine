using BEngine.UIElements;
using UiBox = BEngine.UIElements.Box;
using UiLabel = BEngine.UIElements.Label;

namespace BEngine.Editor;

/// <summary>A retained UIElements time ruler and track canvas for package-defined time-based editors.</summary>
public sealed class TrackEditorCanvas : VisualElement
{
    private const float RulerHeight = 24;
    private const float MarkerHeight = 21;
    private const float RowHeight = 31;
    private const float TimeInset = 10;
    private static readonly UIColor Background = new(30, 33, 36);
    private static readonly UIColor Ruler = new(48, 52, 56);
    private static readonly UIColor MarkerBackground = new(38, 42, 46);
    private static readonly UIColor Selection = new(250, 210, 83);
    private readonly List<TrackEditorTrack> _tracks = [];
    private readonly List<TrackEditorMarker> _markers = [];
    private string _gesture = string.Empty;
    private Vector2 _start;
    private Fix64 _startTime;
    private Fix64 _startScroll;
    private Hit? _activeHit;
    private Fix64 _curveMin;
    private Fix64 _curveMax = 1;

    public float viewportWidth { get; private set; }
    public float viewportHeight { get; private set; }
    public Fix64 startTime { get; set; }
    public Fix64 pixelsPerSecond { get; set; } = 200;
    public Fix64 verticalScroll { get; set; }
    public Fix64 playhead { get; set; }
    public Fix64 frameRate { get; set; } = 60;
    public float sidebarWidth { get; set; }
    public string sidebarHeader { get; set; } = "Add Property";
    public string sidebarFooter { get; set; } = "Dopesheet";
    public string markerLabel { get; set; } = "Events";
    public bool curves { get; set; }
    public IReadOnlyList<TrackEditorTrack> tracks => _tracks;
    public IReadOnlyList<TrackEditorMarker> markers => _markers;

    public event Action<Fix64>? timeChanged;
    public event Action<Fix64, Fix64, Fix64>? viewChanged;
    public event Action<object, bool>? keySelected;
    public event Action? keyDragStarted;
    public event Action<Fix64, Fix64>? keysDragged;
    public event Action? keyDragEnded;
    public event Action<object>? tangentDragStarted;
    public event Action<object, bool, Fix64>? tangentDragged;
    public event Action? tangentDragEnded;
    public event Action<object, bool>? markerSelected;
    public event Action<object, Fix64>? markerDragged;
    public event Action? gestureEnded;
    public event Action<object, bool>? clipSelected;
    public event Action<object, bool>? trackSelected;
    public event Action<object, TrackEditorHeaderAction>? trackHeaderAction;
    public event Action<object, Fix64>? addClipRequested;
    public event Action<object>? clipDragStarted;
    public event Action<object, Fix64>? clipDragged;
    public event Action<object, Fix64>? clipStartTrimmed;
    public event Action<object, Fix64>? clipTrimmed;
    public event Action<object, Fix64>? clipBlendInDragged;
    public event Action<object, Fix64>? clipBlendOutDragged;
    public event Action<int, Fix64>? addKeyRequested;
    public event Action<Fix64>? addMarkerRequested;
    public event Action<IReadOnlyList<object>, bool>? marqueeSelected;
    public event Action<Vector2>? contextRequested;
    public event Action<KeyDownEvent>? keyPressed;

    public TrackEditorCanvas()
    {
        style.backgroundColor = Background;
        style.overflow = Overflow.Hidden;
        RegisterCallback<PointerDownEvent>(OnPointerDown);
        RegisterCallback<PointerMoveEvent>(OnPointerMove);
        RegisterCallback<PointerUpEvent>(OnPointerUp);
        RegisterCallback<WheelEvent>(OnWheel);
        RegisterCallback<ContextClickEvent>(evt => contextRequested?.Invoke(evt.position));
        RegisterCallback<KeyDownEvent>(evt => keyPressed?.Invoke(evt));
    }

    public void SetContent(float width, float height, IEnumerable<TrackEditorTrack> tracks,
        IEnumerable<TrackEditorMarker>? markers = null)
    {
        viewportWidth = Math.Max(1, width);
        viewportHeight = Math.Max(1, height);
        style.width = (Fix64)viewportWidth;
        style.height = (Fix64)viewportHeight;
        _tracks.Clear(); _tracks.AddRange(tracks);
        _markers.Clear(); if (markers is not null) _markers.AddRange(markers);
        DrawContent();
    }

    private void DrawContent()
    {
        Clear();
        Add(Rect(0, 0, viewportWidth, RulerHeight, Ruler));
        Add(Rect(0, RulerHeight, viewportWidth, MarkerHeight, MarkerBackground));
        var graphTop = RulerHeight + MarkerHeight;
        Add(Rect(0, graphTop, viewportWidth, viewportHeight - graphTop, Background));
        if (sidebarWidth > 0)
        {
            Add(Rect(0, 0, sidebarWidth, viewportHeight, new UIColor(45, 49, 53)));
            Add(Rect(Math.Max(0, sidebarWidth - 1), 0, 1, viewportHeight, new UIColor(87, 94, 99)));
            Add(Text(sidebarHeader, 6, 3, Math.Max(1, sidebarWidth - 12), 18,
                new UIColor(217, 222, 225), 11));
            Add(Text(curves ? "Curves" : sidebarFooter, 6, viewportHeight - 21,
                Math.Max(1, sidebarWidth - 12), 18, new UIColor(180, 188, 193), 11));
            Add(Text(markerLabel, 6, RulerHeight + 2, Math.Max(1, sidebarWidth - 12), 18,
                new UIColor(180, 188, 193), 11));
        }
        DrawRuler();
        if (curves) DrawCurves(graphTop);
        else DrawTracks(graphTop);
        DrawMarkers();
        var cursor = X(playhead);
        if (cursor >= 0 && cursor <= viewportWidth)
        {
            Add(Rect(cursor, 0, 1, viewportHeight, new UIColor(225, 229, 231)));
            Add(Text("\u25bc", cursor - 6, 0, 13, 17, new UIColor(241, 245, 246), 12));
        }
    }

    private void DrawRuler()
    {
        var rate = Fix64.Clamp(frameRate, 1, 240);
        var step = Fix64.One / rate;
        while (step * pixelsPerSecond < 72) step *= 2;
        var first = Mathf.FloorToInt(startTime / step);
        var count = Math.Min(300, Mathf.CeilToInt((Fix64)viewportWidth / (step * pixelsPerSecond)) + 3);
        for (var index = 0; index < count; index++)
        {
            var time = (first + index) * step;
            var x = X(time);
            if (x < 0 || x >= viewportWidth) continue;
            Add(Rect(x, 18, 1, 6, new UIColor(139, 149, 157)));
            var frame = Mathf.RoundToInt(time * rate);
            var fps = Math.Max(1, Mathf.RoundToInt(rate));
            Add(Text($"{frame / fps}:{frame % fps:00}", x + 3, 2, 64, 17,
                new UIColor(180, 187, 192), 11));
        }
    }

    private void DrawTracks(float graphTop)
    {
        var first = Math.Max(0, Mathf.FloorToInt(verticalScroll / (Fix64)RowHeight));
        var last = Math.Min(_tracks.Count, first + (int)Math.Ceiling((viewportHeight - graphTop) / RowHeight) + 1);
        for (var row = first; row < last; row++)
        {
            var y = graphTop + row * RowHeight - (float)verticalScroll;
            var track = _tracks[row];
            Add(Rect(sidebarWidth, y, viewportWidth - sidebarWidth, RowHeight - 1,
                row % 2 == 0 ? new UIColor(34, 38, 42) : new UIColor(38, 42, 46),
                new Hit(HitKind.Track, row, track.id)));
            if (sidebarWidth > 0)
            {
                Add(Rect(0, y, sidebarWidth - 1, RowHeight - 1,
                    track.selected ? new UIColor(61, 82, 97) : row % 2 == 0
                        ? new UIColor(45, 49, 53) : new UIColor(49, 53, 57),
                    new Hit(HitKind.Track, row, track.id)));
                Add(Text(track.name, 6, y + 3, Math.Max(1, sidebarWidth - 55), RowHeight - 7,
                    track.muted ? new UIColor(139, 146, 150) : new UIColor(210, 216, 219),
                    11, new Hit(HitKind.Track, row, track.id)));
                if (sidebarWidth >= 100)
                {
                    var mute = Text("M", sidebarWidth - 43, y + 5, 17, 20,
                        track.muted ? Selection : new UIColor(157, 166, 171), 11,
                        new Hit(HitKind.TrackMute, row, track.id));
                    mute.tooltip = track.muted ? "Unmute track" : "Mute track";
                    Add(mute);
                    var locked = Text("L", sidebarWidth - 23, y + 5, 17, 20,
                        track.locked ? Selection : new UIColor(157, 166, 171), 11,
                        new Hit(HitKind.TrackLock, row, track.id));
                    locked.tooltip = track.locked ? "Unlock track" : "Lock track";
                    Add(locked);
                }
            }
            foreach (var clip in track.clips)
            {
                var x = X(clip.start);
                var width = Math.Max(6, (float)(clip.duration * pixelsPerSecond));
                if (x + width < 0 || x > viewportWidth) continue;
                var box = Rect(x, y + 3, width, RowHeight - 7, clip.selected ? Selection : clip.color,
                    new Hit(HitKind.Clip, row, clip.id));
                box.style.borderWidth = 1;
                box.style.borderColor = new UIColor(16, 19, 22);
                Add(box);
            }
            foreach (var clip in track.clips)
            {
                var x = X(clip.start);
                var width = Math.Max(6, (float)(clip.duration * pixelsPerSecond));
                if (x + width < 0 || x > viewportWidth) continue;
                DrawClipBlend(clip, x, y, width, row);
            }
            foreach (var clip in track.clips)
            {
                var x = X(clip.start);
                var width = Math.Max(6, (float)(clip.duration * pixelsPerSecond));
                if (x + width < 0 || x > viewportWidth) continue;
                var nextStart = track.clips.Where(item => item.start > clip.start &&
                        item.start < clip.start + clip.duration)
                    .Select(item => (float)((item.start - clip.start) * pixelsPerSecond))
                    .DefaultIfEmpty(width).Min();
                Add(Text(clip.name, x + 6, y + 5, Math.Max(1, Math.Min(width, nextStart) - 12), RowHeight - 11,
                    new UIColor(19, 24, 27), 12, new Hit(HitKind.Clip, row, clip.id)));
                Add(Rect(x, y + 3, 5, RowHeight - 7, new UIColor(22, 28, 31, 80),
                    new Hit(HitKind.ClipStart, row, clip.id)));
                Add(Rect(x + width - 5, y + 3, 5, RowHeight - 7, new UIColor(22, 28, 31, 80),
                    new Hit(HitKind.ClipEnd, row, clip.id)));
            }
            foreach (var clip in track.clips.Where(item => item.selected))
            {
                var x = X(clip.start);
                var width = Math.Max(6, (float)(clip.duration * pixelsPerSecond));
                if (x + width < 0 || x > viewportWidth) continue;
                if (clip.selected && clip.blendEditable && width >= 36)
                {
                    var blendIn = Math.Clamp((float)(clip.blendIn * pixelsPerSecond), 6, width - 12);
                    var blendOut = Math.Clamp((float)(clip.blendOut * pixelsPerSecond), 6, width - 12);
                    var inHandle = Rect(x + blendIn - 3, y + 3, 7, 7, Selection,
                        new Hit(HitKind.ClipBlendIn, row, clip.id, clip.blendIn));
                    inHandle.tooltip = "Drag to adjust clip blend in";
                    Add(inHandle);
                    var outHandle = Rect(x + width - blendOut - 3, y + RowHeight - 12, 7, 7, Selection,
                        new Hit(HitKind.ClipBlendOut, row, clip.id, clip.blendOut));
                    outHandle.tooltip = "Drag to adjust clip blend out";
                    Add(outHandle);
                }
            }
            foreach (var key in track.keys)
            {
                var x = X(key.time);
                if (x < -8 || x > viewportWidth + 8) continue;
                DrawDiamond(x, y + (RowHeight - 10) / 2,
                    key.selected ? Selection : track.color, new Hit(HitKind.Key, row, key.id));
            }
        }
    }

    private void DrawClipBlend(TrackEditorClip clip, float x, float y, float width, int row)
    {
        var incoming = Math.Min(width, (float)(clip.blendIn * pixelsPerSecond));
        var outgoing = Math.Min(width, (float)(clip.blendOut * pixelsPerSecond));
        if (incoming > 1)
        {
            Add(Rect(x, y + 3, incoming, RowHeight - 7, new UIColor(19, 27, 31, 90),
                new Hit(HitKind.Clip, row, clip.id)));
            DrawBlendLine(x, y + RowHeight - 5, x + incoming, y + 3, clip, row);
        }
        if (outgoing > 1)
        {
            Add(Rect(x + width - outgoing, y + 3, outgoing, RowHeight - 7,
                new UIColor(19, 27, 31, 90), new Hit(HitKind.Clip, row, clip.id)));
            DrawBlendLine(x + width - outgoing, y + 3, x + width, y + RowHeight - 5, clip, row);
        }
    }

    private void DrawBlendLine(float fromX, float fromY, float toX, float toY,
        TrackEditorClip clip, int row)
    {
        var segments = Math.Clamp((int)Math.Ceiling(toX - fromX), 2, 100);
        for (var index = 0; index <= segments; index++)
        {
            var fraction = index / (float)segments;
            Add(Rect(fromX + (toX - fromX) * fraction,
                fromY + (toY - fromY) * fraction, 2, 2,
                new UIColor(243, 245, 246), new Hit(HitKind.Clip, row, clip.id)));
        }
    }

    private void DrawCurves(float graphTop)
    {
        var visibleTracks = _tracks.Where(track => track.curveVisible).ToArray();
        var values = visibleTracks.SelectMany(track => track.keys).Select(key => key.value).ToArray();
        var min = values.DefaultIfEmpty(0).Min();
        var max = values.DefaultIfEmpty(1).Max();
        var padding = Fix64.Max((Fix64).25, (max - min) * (Fix64).15);
        if (_gesture != "keys") { _curveMin = min - padding; _curveMax = max + padding; }
        if (_curveMin == _curveMax) _curveMax = _curveMin + 1;
        var graphHeight = Math.Max(1, viewportHeight - graphTop);
        for (var line = 0; line <= 4; line++)
        {
            var y = graphTop + graphHeight * line / 4;
            Add(Rect(0, y, viewportWidth, 1, new UIColor(63, 68, 73)));
            var value = _curveMax - (_curveMax - _curveMin) * line / 4;
            Add(Text(((float)value).ToString("0.##"), 3, y + 1, 56, 16,
                new UIColor(143, 153, 159), 10));
        }
        foreach (var (track, row) in _tracks.Select((track, row) => (track, row)))
        {
            if (!track.curveVisible || track.sample is null) continue;
            var samples = Math.Clamp((int)viewportWidth / 4, 2, 300);
            var previousX = sidebarWidth;
            var previousY = CurveY(track.sample(Time((Fix64)previousX)), graphTop, graphHeight);
            for (var point = 1; point <= samples; point++)
            {
                var x = sidebarWidth + (viewportWidth - sidebarWidth) * point / samples;
                var y = CurveY(track.sample(Time((Fix64)x)), graphTop, graphHeight);
                var segments = Math.Clamp((int)Math.Ceiling(Math.Max(x - previousX,
                    Math.Abs(y - previousY)) / 2), 1, 512);
                for (var segment = 0; segment <= segments; segment++)
                {
                    var t = segment / (float)segments;
                    var lineY = previousY + (y - previousY) * t;
                    if (lineY >= graphTop && lineY < viewportHeight - 2)
                        Add(Rect(previousX + (x - previousX) * t, lineY, 2, 2, track.color));
                }
                previousX = x;
                previousY = y;
            }
            foreach (var key in track.keys)
            {
                var x = X(key.time);
                if (x < -8 || x > viewportWidth + 8) continue;
                var y = CurveY(key.value, graphTop, graphHeight);
                DrawDiamond(x, y - 5, key.selected ? Selection : track.color,
                    new Hit(HitKind.Key, row, key.id));
                if (!key.selected) continue;
                DrawTangentHandle(key, row, x, y, graphTop, graphHeight, true);
                DrawTangentHandle(key, row, x, y, graphTop, graphHeight, false);
            }
        }
    }

    private void DrawTangentHandle(TrackEditorKey key, int row, float x, float y,
        float graphTop, float graphHeight, bool input)
    {
        const float handleDistance = 32;
        var handleX = x + (input ? -handleDistance : handleDistance);
        var tangent = input ? key.inTangent : key.outTangent;
        var handleValue = key.value + tangent * (Fix64)((input ? -handleDistance : handleDistance) / (float)pixelsPerSecond);
        var handleY = CurveY(handleValue, graphTop, graphHeight);
        if (handleX < sidebarWidth + 3 || handleX > viewportWidth - 3 ||
            handleY < graphTop + 3 || handleY > viewportHeight - 3) return;
        var segments = Math.Clamp((int)Math.Ceiling(Math.Max(Math.Abs(handleX - x),
            Math.Abs(handleY - y)) / 2), 1, 200);
        for (var segment = 0; segment <= segments; segment++)
        {
            var t = segment / (float)segments;
            var lineY = y + (handleY - y) * t;
            if (lineY >= graphTop && lineY < viewportHeight - 2)
                Add(Rect(x + (handleX - x) * t, lineY, 2, 2, Selection));
        }
        Add(Rect(handleX - 4, handleY - 4, 9, 9, Selection,
            new Hit(input ? HitKind.TangentIn : HitKind.TangentOut, row, key.id)));
    }

    private void DrawMarkers()
    {
        foreach (var marker in _markers)
        {
            var x = X(marker.time);
            if (x < -8 || x > viewportWidth + 8) continue;
            DrawDiamond(x, RulerHeight + 5,
                marker.selected ? Selection : new UIColor(111, 190, 222),
                new Hit(HitKind.Marker, -1, marker.id));
        }
    }

    private void DrawDiamond(float centerX, float top, UIColor color, Hit hit)
    {
        ReadOnlySpan<int> widths = [2, 6, 10, 6, 2];
        for (var row = 0; row < widths.Length; row++)
            Add(Rect(centerX - widths[row] / 2f, top + row * 2,
                widths[row], 2, color, hit));
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        _start = evt.position;
        _startTime = startTime;
        _startScroll = verticalScroll;
        _activeHit = evt.target?.userData as Hit;
        if (evt.button == 2 || evt.button == 0 && evt.altKey) { _gesture = "pan"; return; }
        if (evt.button == 1)
        {
            if (_activeHit is { kind: HitKind.Key } contextKey)
                keySelected?.Invoke(contextKey.id, false);
            else if (_activeHit is { kind: HitKind.Clip or HitKind.ClipStart or HitKind.ClipEnd or
                         HitKind.ClipBlendIn or HitKind.ClipBlendOut } contextClip)
                clipSelected?.Invoke(contextClip.id, false);
            else if (_activeHit is { kind: HitKind.Track } contextTrack)
                trackSelected?.Invoke(contextTrack.id, false);
            return;
        }
        if (evt.button != 0) return;
        if (_activeHit is { kind: HitKind.TrackMute or HitKind.TrackLock } header)
        {
            trackHeaderAction?.Invoke(header.id, header.kind == HitKind.TrackMute
                ? TrackEditorHeaderAction.Mute : TrackEditorHeaderAction.Lock);
            _gesture = string.Empty;
        }
        else if (_activeHit is { kind: HitKind.TangentIn or HitKind.TangentOut } tangent)
        {
            tangentDragStarted?.Invoke(tangent.id);
            _gesture = "tangent";
        }
        else if (_activeHit is { kind: HitKind.Key } key)
        {
            keySelected?.Invoke(key.id, evt.shiftKey);
            keyDragStarted?.Invoke();
            _gesture = "keys";
        }
        else if (_activeHit is { kind: HitKind.Marker } marker)
        {
            markerSelected?.Invoke(marker.id, evt.shiftKey);
            _gesture = "marker";
        }
        else if (_activeHit is { kind: HitKind.Clip or HitKind.ClipStart or HitKind.ClipEnd or
                     HitKind.ClipBlendIn or HitKind.ClipBlendOut } clip)
        {
            clipSelected?.Invoke(clip.id, evt.shiftKey);
            if (_tracks[clip.row].locked) return;
            clipDragStarted?.Invoke(clip.id);
            _gesture = clip.kind switch
            {
                HitKind.ClipStart => "trim-start",
                HitKind.ClipEnd => "trim",
                HitKind.ClipBlendIn => "blend-in",
                HitKind.ClipBlendOut => "blend-out",
                _ => "clip"
            };
        }
        else if (_activeHit is { kind: HitKind.Track } track)
        {
            trackSelected?.Invoke(track.id, evt.shiftKey);
            if (evt.clickCount >= 2 && evt.position.x >= (Fix64)sidebarWidth && !_tracks[track.row].locked)
                addClipRequested?.Invoke(track.id, Time(evt.position.x));
            _gesture = string.Empty;
        }
        else if (evt.position.y < (Fix64)(RulerHeight + MarkerHeight))
        {
            timeChanged?.Invoke(Time(evt.position.x));
            if (evt.position.y >= (Fix64)RulerHeight && evt.clickCount >= 2)
                addMarkerRequested?.Invoke(Time(evt.position.x));
            _gesture = "scrub";
        }
        else if (evt.clickCount >= 2)
        {
            var row = (int)((evt.position.y - (Fix64)(RulerHeight + MarkerHeight) + verticalScroll) / (Fix64)RowHeight);
            if (row >= 0 && row < _tracks.Count) addKeyRequested?.Invoke(row, Time(evt.position.x));
        }
        else _gesture = "marquee";
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (_gesture.Length == 0) return;
        var delta = evt.position - _start;
        switch (_gesture)
        {
            case "pan":
                startTime = Fix64.Max(0, _startTime - delta.x / pixelsPerSecond);
                verticalScroll = Fix64.Max(0, _startScroll - delta.y);
                viewChanged?.Invoke(startTime, pixelsPerSecond, verticalScroll);
                break;
            case "scrub": timeChanged?.Invoke(Time(evt.position.x)); break;
            case "keys": keysDragged?.Invoke(delta.x / pixelsPerSecond,
                curves ? -delta.y / Fix64.Max(1, (Fix64)(viewportHeight - RulerHeight - MarkerHeight)) *
                    (_curveMax - _curveMin) : 0); break;
            case "tangent" when _activeHit is { } tangent:
                var graphHeight = Fix64.Max(1, (Fix64)(viewportHeight - RulerHeight - MarkerHeight - 24));
                var deltaSlope = -delta.y * (_curveMax - _curveMin) / graphHeight *
                    pixelsPerSecond / 32;
                tangentDragged?.Invoke(tangent.id, tangent.kind == HitKind.TangentIn,
                    tangent.kind == HitKind.TangentIn ? -deltaSlope : deltaSlope);
                break;
            case "marker" when _activeHit is { } marker:
                markerDragged?.Invoke(marker.id, delta.x / pixelsPerSecond); break;
            case "clip" when _activeHit is { } clip:
                clipDragged?.Invoke(clip.id, delta.x / pixelsPerSecond); break;
            case "trim-start" when _activeHit is { } start:
                clipStartTrimmed?.Invoke(start.id, delta.x / pixelsPerSecond); break;
            case "trim" when _activeHit is { } end:
                clipTrimmed?.Invoke(end.id, delta.x / pixelsPerSecond); break;
            case "blend-in" when _activeHit is { } blendIn:
                clipBlendInDragged?.Invoke(blendIn.id, Fix64.Max(0, blendIn.value +
                    delta.x / pixelsPerSecond)); break;
            case "blend-out" when _activeHit is { } blendOut:
                clipBlendOutDragged?.Invoke(blendOut.id, Fix64.Max(0, blendOut.value -
                    delta.x / pixelsPerSecond)); break;
        }
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        if (_gesture == "marquee")
        {
            var left = Fix64.Min(_start.x, evt.position.x);
            var right = Fix64.Max(_start.x, evt.position.x);
            var top = Fix64.Min(_start.y, evt.position.y);
            var bottom = Fix64.Max(_start.y, evt.position.y);
            var selected = new List<object>();
            for (var row = 0; row < _tracks.Count; row++)
            {
                var y = RulerHeight + MarkerHeight + row * RowHeight - (float)verticalScroll + RowHeight / 2;
                if ((Fix64)y < top || (Fix64)y > bottom) continue;
                selected.AddRange(_tracks[row].keys.Where(key => (Fix64)X(key.time) >= left && (Fix64)X(key.time) <= right)
                    .Select(key => key.id));
            }
            marqueeSelected?.Invoke(selected, false);
        }
        CancelGesture();
    }

    /// <summary>Ends active dragging without applying a pending marquee selection.</summary>
    public void CancelGesture()
    {
        var gesture = _gesture;
        _gesture = string.Empty;
        _activeHit = null;
        if (gesture == "keys") keyDragEnded?.Invoke();
        if (gesture == "tangent") tangentDragEnded?.Invoke();
        gestureEnded?.Invoke();
    }

    private void OnWheel(WheelEvent evt)
    {
        var anchor = Time(evt.position.x);
        pixelsPerSecond = Fix64.Clamp(pixelsPerSecond * (Fix64)Math.Pow(1.12, -(double)evt.delta.y), 12, 3000);
        startTime = Fix64.Max(0, anchor - (evt.position.x - (Fix64)TimeInset) / pixelsPerSecond);
        viewChanged?.Invoke(startTime, pixelsPerSecond, verticalScroll);
    }

    private float X(Fix64 time) => sidebarWidth + TimeInset + (float)((time - startTime) * pixelsPerSecond);
    private Fix64 Time(Fix64 x) => Fix64.Max(0, startTime + (x - (Fix64)(sidebarWidth + TimeInset)) / pixelsPerSecond);
    private float CurveY(Fix64 value, float top, float height) =>
        top + height - 12 - (float)((value - _curveMin) / (_curveMax - _curveMin)) * Math.Max(1, height - 24);

    private UiBox Rect(float x, float y, float width, float height, UIColor color, Hit? hit = null)
    {
        var element = new UiBox();
        Place(element, x, y, width, height);
        element.style.backgroundColor = color;
        element.userData = hit;
        AttachCapturedInput(element);
        return element;
    }

    private UiLabel Text(string value, float x, float y, float width, float height,
        UIColor color, float size, Hit? hit = null)
    {
        var element = new UiLabel(value);
        Place(element, x, y, width, height);
        element.style.color = color;
        element.style.fontSize = (Fix64)size;
        element.userData = hit;
        AttachCapturedInput(element);
        return element;
    }

    private static void Place(VisualElement element, float x, float y, float width, float height)
    {
        element.style.position = Position.Absolute;
        element.style.left = (Fix64)x;
        element.style.top = (Fix64)y;
        element.style.width = Fix64.Max(1, (Fix64)width);
        element.style.height = Fix64.Max(1, (Fix64)height);
    }

    private void AttachCapturedInput(VisualElement element)
    {
        element.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (_gesture.Length == 0 || !ReferenceEquals(evt.target, element)) return;
            OnPointerMove(evt);
            evt.StopPropagation();
        });
        element.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (_gesture.Length == 0 || !ReferenceEquals(evt.target, element)) return;
            OnPointerUp(evt);
            evt.StopPropagation();
        });
    }

    private enum HitKind { Key, TangentIn, TangentOut, Marker, Clip, ClipStart, ClipEnd,
        ClipBlendIn, ClipBlendOut, Track, TrackMute, TrackLock }
    private sealed record Hit(HitKind kind, int row, object id, Fix64 value = default);
}
