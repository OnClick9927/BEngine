using BEngine.Rendering.Rhi;
using System.Text.Json;

namespace BEngine.Editor;

public sealed class GpuPassProfilerWindow : EditorWindow
{
    private IDisposable? _recording;
    private Vector2 _scroll;
    private readonly EditorScrollView _scrollView = new();
    private string _search = "";
    private string _status = "";
    private bool _live = true, _sortByCost;
    private long _version = -1;
    private int _selected;
    private GraphicsPassTiming[] _samples = [];
    private (long Frame, GraphicsBackend Backend)[] _frames = [];
    private readonly List<GraphicsPassTiming> _visible = [];
    [MenuItem("Window/Analysis/GPU Passes", false, 214)]
    private static void Open() => GetWindow<GpuPassProfilerWindow>("GPU Passes");
    protected override void OnEnable() { _recording = GraphicsFrameTimingService.Acquire(); minSize = new(350, 180); }
    protected override void OnDisable() { _recording?.Dispose(); _recording = null; }
    protected override void Update()
    {
        if (_live && GraphicsPassTimingService.Version != _version) { Refresh(); Repaint(); }
    }
    private void Refresh()
    {
        _version = GraphicsPassTimingService.Version;
        _samples = GraphicsPassTimingService.Snapshot();
        _frames = _samples.Select(sample => (sample.Frame, sample.Backend)).Distinct().ToArray();
        _selected = Math.Max(0, _frames.Length - 1);
        Filter();
    }
    private void Filter()
    {
        _visible.Clear();
        if (_frames.Length == 0) return;
        var selected = _frames[_selected];
        foreach (var sample in _samples)
            if (sample.Frame == selected.Frame && sample.Backend == selected.Backend && sample.Name.Contains(_search, StringComparison.OrdinalIgnoreCase))
                _visible.Add(sample);
        if (_sortByCost) _visible.Sort(static (a, b) => b.Milliseconds.CompareTo(a.Milliseconds));
    }
    protected override void OnGUI()
    {
        if (_live && GraphicsPassTimingService.Version != _version) Refresh();
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button(_live ? "Freeze" : "Live", EditorStyles.toolbarButton)) { _live = !_live; if (_live) Refresh(); }
        if (GUILayout.Button(_sortByCost ? "Execution order" : "Sort by cost", EditorStyles.toolbarButton)) { _sortByCost = !_sortByCost; Filter(); }
        using (new EditorGUI.DisabledScope(_samples.Length == 0))
            if (GUILayout.Button("Export", EditorStyles.toolbarButton)) Export();
        GUILayout.EndHorizontal();
        var search = EditorGUILayout.TextField("Search", _search);
        if (search != _search) { _search = search; Filter(); }
        if (_frames.Length == 0) { GUILayout.Label("Waiting for GPU timestamp samples. Run a Vulkan or OpenGL scene."); return; }
        var selected = EditorGUILayout.IntSlider("Captured frame", _selected, 0, _frames.Length - 1);
        if (selected != _selected) { _selected = selected; _live = false; Filter(); }
        var latest = _frames[_selected];
        GUILayout.Label($"{latest.Backend} — Frame {latest.Frame}", EditorStyles.boldLabel);
        _scroll = _scrollView.Begin(_scroll);
        try
        {
            foreach (var sample in _visible)
                GUILayout.Label($"{sample.Name}: {sample.Milliseconds:F3} ms");
            GUILayout.Label("Nested passes overlap; do not add their durations.");
            if (_status.Length > 0) GUILayout.Label(_status);
        }
        finally { _scrollView.End(); }
    }
    private void Export()
    {
        var path = FileDialogUtility.Current.SaveFile("Export GPU passes", "", "JSON (*.json)|*.json", "gpu-passes.json");
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.WriteAllText(path, JsonSerializer.Serialize(_samples, new JsonSerializerOptions { WriteIndented = true })); _status = "Capture exported."; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _status = error.Message; }
    }
}
