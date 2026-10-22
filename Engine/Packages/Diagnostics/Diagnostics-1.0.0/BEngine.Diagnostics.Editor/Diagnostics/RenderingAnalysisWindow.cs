using System.Globalization;
using System.Text.Json;

namespace BEngine.Editor;

/// <summary>Captured frame inspection and baseline comparison without querying live render objects.</summary>
public sealed class RenderingAnalysisWindow : EditorWindow
{
    private readonly EditorProfilerFrame[] _frames = new EditorProfilerFrame[512];
    private readonly EditorScrollView _scroll = new();
    private Vector2 _position;
    private long _version = -1;
    private int _count, _selected;
    private bool _live = true;
    private EditorProfilerFrame? _baseline;
    private string _status = "", _targetFps = "60";
    [MenuItem("Window/Analysis/Rendering Analysis", false, 215)]
    private static void Open() => GetWindow<RenderingAnalysisWindow>("Rendering Analysis");
    protected override void OnEnable() => minSize = new(420, 240);
    protected override void Update()
    {
        if (!_live || EditorProfiler.GetMetadata().Version == _version) return;
        Refresh(); Repaint();
    }
    private void Refresh()
    {
        var metadata = EditorProfiler.GetMetadata();
        if (_version == metadata.Version) return;
        _count = EditorProfiler.CopyFrames(_frames, out EditorProfilerMetadata captured);
        _version = captured.Version;
        if (_live) _selected = Math.Max(0, _count - 1);
        else _selected = Math.Clamp(_selected, 0, Math.Max(0, _count - 1));
    }
    protected override void OnGUI()
    {
        if (_live) Refresh();
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button(EditorProfiler.Recording ? "Stop" : "Record", EditorStyles.toolbarButton))
            EditorProfiler.Recording = !EditorProfiler.Recording;
        if (GUILayout.Button(_live ? "Freeze" : "Live", EditorStyles.toolbarButton)) { _live = !_live; if (_live) Refresh(); }
        using (new EditorGUI.DisabledScope(_count == 0))
        {
            if (GUILayout.Button("Baseline", EditorStyles.toolbarButton)) _baseline = _frames[_selected];
            if (GUILayout.Button("Export", EditorStyles.toolbarButton)) Export();
        }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Frame Debugger", EditorStyles.toolbarButton)) GetWindow<FrameDebuggerWindow>("Frame Debugger");
        if (GUILayout.Button("GPU Passes", EditorStyles.toolbarButton)) GetWindow<GpuPassProfilerWindow>("GPU Passes");
        GUILayout.EndHorizontal();
        if (_count == 0) { GUILayout.Label("Record frames to inspect rendering costs and compare a baseline."); return; }
        var selected = EditorGUILayout.IntSlider("Captured frame", _selected, 0, _count - 1);
        if (selected != _selected) { _selected = selected; _live = false; }
        _targetFps = EditorGUILayout.TextField("Target FPS", _targetFps);
        var frame = _frames[_selected]; var stats = frame.RenderStatistics;
        _position = _scroll.Begin(_position);
        try
        {
            GUILayout.Label($"Frame {frame.FrameIndex} — {frame.TimestampUtc:O}", EditorStyles.boldLabel);
            Row("CPU frame", frame.FrameMilliseconds, _baseline?.FrameMilliseconds);
            Row("Collect", stats.CollectMilliseconds, _baseline?.RenderStatistics.CollectMilliseconds);
            Row("Sort", stats.SortMilliseconds, _baseline?.RenderStatistics.SortMilliseconds);
            Row("Core geometry", stats.GeometryMilliseconds, _baseline?.RenderStatistics.GeometryMilliseconds);
            Row("Submit (includes packages)", stats.SubmitMilliseconds, _baseline?.RenderStatistics.SubmitMilliseconds);
            Row("Present CPU", frame.PresentMilliseconds, _baseline?.PresentMilliseconds);
            if (frame.HasGpuTiming) Row("GPU frame", frame.GpuMilliseconds, _baseline is { HasGpuTiming: true } baseline ? baseline.GpuMilliseconds : null);
            else GUILayout.Label("GPU timing unavailable for this captured frame.");
            GUILayout.Label($"Cameras: {stats.CameraCount}   Visible: {stats.VisibleSubmissionCount}   Batches: {stats.BatchCount}");
            GUILayout.Label(stats.HasCompleteDrawStatistics ? $"Draws: {stats.DrawCallCount}   Vertices: {stats.VertexCount}" : "Complete draw counters unavailable.");
            GUILayout.Label($"Core uploads: {stats.CoreGeometryUploadCount}   Bytes: {stats.CoreGeometryUploadBytes:N0}");
            GUILayout.Label($"Core quads built: {stats.CoreGeometryBuiltQuadCount:N0}   Reused: {stats.CoreGeometryReusedQuadCount:N0}");
            GUILayout.Label($"Sprite world geometry built: {stats.CoreSpriteWorldGeometryBuiltCount:N0}   Reused: {stats.CoreSpriteWorldGeometryReusedCount:N0}");
            GUILayout.Label($"Sprite culling tests: {stats.CoreSpriteCullingTestCount:N0}   Reused: {stats.CoreSpriteCullingReuseCount:N0}   Culled: {stats.CoreSpriteCulledCount:N0}");
            GUILayout.Label($"Full sorts: {stats.SortFullPassCount:N0}   Linear reuse/ordered passes: {stats.SortReusePassCount:N0}");
            GUILayout.Label($"Adjacent Core ranges merged: {stats.CoreDrawRangeMergeCount:N0}");
            GUILayout.Label($"Managed allocated: {frame.ManagedAllocatedBytes:N0} B");
            if (_baseline is { } b)
                GUILayout.Label($"Baseline frame {b.FrameIndex}: draw delta {stats.DrawCallCount - b.RenderStatistics.DrawCallCount:+0;-0;0}, allocation delta {frame.ManagedAllocatedBytes - b.ManagedAllocatedBytes:+0;-0;0} B");
            if (double.TryParse(_targetFps, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) && fps > 0 && double.IsFinite(fps))
            {
                var budget = 1000 / fps;
                GUILayout.Label($"CPU budget {budget:F2} ms: {(frame.FrameMilliseconds > budget ? "OVER" : "within")}; largest CPU rendering stage: {LargestStage(frame)}");
            }
            GUILayout.Label("Core uploads exclude package-owned meshes. Present CPU time is not GPU execution time.");
            if (_status.Length > 0) GUILayout.Label(_status);
        }
        finally { _scroll.End(); }
    }
    private static void Row(string name, double value, double? baseline) =>
        GUILayout.Label($"{name}: {value:F3} ms" + (baseline is { } b ? $"   Δ {value - b:+0.000;-0.000;0.000} ms" : ""));
    public static string LargestStage(EditorProfilerFrame frame)
    {
        var stats = frame.RenderStatistics;
        var name = "Collect"; var value = stats.CollectMilliseconds;
        if (stats.SortMilliseconds > value) { name = "Sort"; value = stats.SortMilliseconds; }
        if (stats.GeometryMilliseconds > value) { name = "Core geometry"; value = stats.GeometryMilliseconds; }
        if (stats.SubmitMilliseconds > value) name = "Submit";
        return name;
    }
    public static string SerializeCapture(ReadOnlySpan<EditorProfilerFrame> frames, EditorProfilerFrame? baseline) =>
        JsonSerializer.Serialize(new { Baseline = baseline, Frames = frames.ToArray() }, new JsonSerializerOptions { WriteIndented = true });
    private void Export()
    {
        var path = FileDialogUtility.Current.SaveFile("Export rendering capture", "", "JSON (*.json)|*.json", "rendering-capture.json");
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.WriteAllText(path, SerializeCapture(_frames.AsSpan(0, _count), _baseline)); _status = "Capture exported."; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _status = error.Message; }
    }
}
