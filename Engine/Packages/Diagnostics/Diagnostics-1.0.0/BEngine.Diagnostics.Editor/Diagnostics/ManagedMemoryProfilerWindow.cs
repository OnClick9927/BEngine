using System.Text.Json;

namespace BEngine.Editor.Diagnostics;

public sealed class ManagedMemoryProfilerWindow : EditorWindow
{
    private sealed record Row(ulong? Address, string Text);
    private ManagedHeapSnapshot? _snapshot;
    private ManagedHeapAnalysis? _analysis, _baseline;
    private readonly EditorScrollView _scroll = new(); private Vector2 _position;
    private string _filter = "", _status = "Capture a CLR heap or open a managed dump.";
    private Row[] _rows = [];
    private bool _busy, _retained = true;
    private int _view, _page, _generation;
    private const int PageSize = 128;
    private CancellationTokenSource? _work;
    [MenuItem("Window/Analysis/CLR Heap", false, 214)]
    private static void Open() => GetWindow<ManagedMemoryProfilerWindow>("CLR Heap").Show();
    protected override void OnDisable() { _work?.Cancel(); _generation++; }
    protected override void OnGUI()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        using (new EditorGUI.DisabledScope(_busy))
        {
            if (GUILayout.Button("Capture", EditorStyles.toolbarButton)) Read(token => ManagedHeapSnapshot.CaptureProcess(Environment.ProcessId, cancellationToken: token));
            if (GUILayout.Button("Open Dump", EditorStyles.toolbarButton))
            {
                var path = EditorUtility.OpenFilePanel("Open managed dump", "", "dmp,core");
                if (!string.IsNullOrWhiteSpace(path)) Read(token => ManagedHeapSnapshot.ReadDump(path, cancellationToken: token));
            }
        }
        using (new EditorGUI.DisabledScope(_snapshot is null || _busy))
        {
            if (GUILayout.Button("Baseline", EditorStyles.toolbarButton)) { _baseline = _analysis; RefreshRows(); }
            if (GUILayout.Button("Save JSON", EditorStyles.toolbarButton)) Save();
        }
        if (_busy && GUILayout.Button("Cancel", EditorStyles.toolbarButton)) { _work?.Cancel(); _generation++; _busy = false; _status = "Cancelled."; }
        GUILayout.FlexibleSpace(); var filter = GUILayout.TextField(_filter, GUILayout.Width(180)); GUILayout.EndHorizontal();
        if (filter != _filter) { _filter = filter; RefreshRows(); }
        GUILayout.BeginHorizontal();
        foreach (var (name, view) in new[] { ("Objects", 0), ("Types", 1), ("Compare", 2) })
            if (GUILayout.Button(name, EditorStyles.miniButton) && _view != view) { _view = view; RefreshRows(); }
        if (GUILayout.Button(_retained ? "Sort: Retained" : "Sort: Size", EditorStyles.miniButton)) { _retained = !_retained; RefreshRows(); }
        GUILayout.EndHorizontal();
        GUILayout.Label(_status, EditorStyles.wordWrappedMiniLabel);
        if (_snapshot is not { } snapshot || _analysis is not { } analysis) return;
        GUILayout.Label($"{snapshot.Objects.Length:N0} objects · {analysis.TotalBytes:N0} B · {snapshot.Roots.Length:N0} roots" + (snapshot.Truncated ? " · incomplete capture: retained estimates may be inaccurate" : ""));
        var pages = Math.Max(1, (_rows.Length + PageSize - 1) / PageSize); _page = Math.Min(_page, pages - 1);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Previous", EditorStyles.miniButton)) { _page = Math.Max(0, _page - 1); _position = default; }
        GUILayout.Label($"Page {_page + 1:N0}/{pages:N0} · {_rows.Length:N0} matches");
        if (GUILayout.Button("Next", EditorStyles.miniButton)) { _page = Math.Min(pages - 1, _page + 1); _position = default; }
        GUILayout.EndHorizontal();
        _position = _scroll.Begin(_position);
        for (var i = _page * PageSize; i < Math.Min(_rows.Length, (_page + 1) * PageSize); i++)
        {
            var row = _rows[i];
            if (GUILayout.Button(row.Text, EditorStyles.miniButton) && row.Address is { } address)
            {
                var path = analysis.PathToRoot(address);
                _status = path.Length == 0 ? "No GC root path in this capture." : "GC root path: " + string.Join(" → ", path.Select(item => $"0x{item:X}"));
            }
        }
        _scroll.End();
    }
    public void DisplaySnapshot(ManagedHeapSnapshot snapshot)
    { _snapshot = snapshot; _analysis = null; Read(_ => snapshot); }
    private void Read(Func<CancellationToken, ManagedHeapSnapshot> read)
    {
        Start("Reading and indexing CLR heap…", token =>
        {
            var snapshot = read(token); var analysis = snapshot.Analyze(token);
            return () => { _snapshot = snapshot; _analysis = analysis; RefreshRows(); };
        });
    }
    private void RefreshRows()
    {
        if (_analysis is not { } analysis || _snapshot is not { } snapshot) return;
        var filter = _filter; var view = _view; var retained = _retained; var baseline = _baseline;
        Start("Filtering heap…", token =>
        {
            Row[] rows;
            if (view == 1) rows = analysis.Types.Where(item => item.Type.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Select(item => new Row(null, $"{item.Bytes:N0} B · {item.Count:N0} objects · {item.Type}")).ToArray();
            else if (view == 2) rows = baseline is null ? [] : analysis.CompareTo(baseline).Where(item => item.Type.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Select(item => new Row(null, $"Δ {item.BytesDelta:+#,0;-#,0;0} B · Δ {item.CountDelta:+#,0;-#,0;0} objects · {item.Type}")).ToArray();
            else
            {
                var matches = new List<ManagedHeapObject>();
                for (var i = 0; i < snapshot.Objects.Length; i++)
                { if ((i & 4095) == 0) token.ThrowIfCancellationRequested(); var item = snapshot.Objects[i]; if (item.Type.Contains(filter, StringComparison.OrdinalIgnoreCase)) matches.Add(item); }
                rows = matches.OrderByDescending(item => retained ? analysis.RetainedBytes(item.Address) ?? 0 : item.Size)
                    .Select(item => new Row(item.Address, $"0x{item.Address:X} · size {item.Size:N0} B · retained {analysis.RetainedBytes(item.Address)?.ToString("N0") ?? "unknown"} B · {item.Type}")).ToArray();
            }
            token.ThrowIfCancellationRequested();
            return () => { _rows = rows; _page = 0; _position = default; _status = view == 2 && baseline is null ? "Set a baseline, then capture another heap to compare type growth." : "Select an object for its cached GC root path."; };
        });
    }
    private void Save()
    {
        if (_snapshot is not { } snapshot) return;
        var directory = Path.Combine(EditorApplication.projectPath, "Library", "Diagnostics");
        Start("Saving snapshot…", token =>
        {
            Directory.CreateDirectory(directory); var path = Path.Combine(directory, "clr-heap-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
            token.ThrowIfCancellationRequested(); File.WriteAllText(path, JsonSerializer.Serialize(snapshot));
            return () => _status = "Saved " + path;
        });
    }
    // Editor analysis can use a background task; game runtime scheduling remains single-threaded.
    private void Start(string status, Func<CancellationToken, Action> action)
    {
        _work?.Cancel(); _work?.Dispose(); _work = new(); var token = _work.Token; var generation = ++_generation;
        _busy = true; _status = status;
        _ = Task.Run(() =>
        {
            Action? apply = null; string? error = null;
            try { apply = action(token); } catch (OperationCanceledException) { error = "Cancelled."; } catch (Exception exception) { error = exception.Message; }
            EditorApplication.delayCall += () =>
            {
                if (isDestroyed || generation != _generation) return;
                _busy = false; if (error is not null) _status = error; else apply?.Invoke(); Repaint();
            };
        });
    }
}
