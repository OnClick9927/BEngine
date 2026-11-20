using BEngine.Instrumentation;
using System.Text.Json;
using BEngine.Diagnostics;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/Window.png")]
[RuntimeTypeAlias("BEngine.App.Editor.MemoryProfilerWindow")]
public sealed class MemoryProfilerWindow : EditorWindow
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { WriteIndented = true };
    private readonly EditorScrollView _listView = new();
    private readonly EditorScrollView _detailsView = new();
    private EngineObjectHeapSnapshot? _snapshot;
    private MemoryProfilerSnapshotView? _view;
    private EngineObjectHeapNode[] _visibleRows = [];
    private int _page;
    private const int PageSize = 128;
    private static readonly string[] ByteUnits = ["B", "KiB", "MiB", "GiB"];
    private EngineObjectHeapSnapshot? _baseline;
    private string _source = "Editor";
    private string _search = string.Empty;
    private string _status = "Capture the Editor heap or capture a connected Player from Remote Player Profiler.";
    private int _selectedSnapshotId;
    private Vector2 _listScroll;
    private Vector2 _detailsScroll;
    private bool _showNativeAllocations;

    [MenuItem("Window/Analysis/Memory Profiler", false, 213)]
    private static void Open() => GetWindow<MemoryProfilerWindow>("Memory Profiler");

    internal static void ShowSnapshot(EngineObjectHeapSnapshot snapshot, string source)
    {
        var window = GetWindow<MemoryProfilerWindow>("Memory Profiler");
        window.SetSnapshot(snapshot, source);
        window.Show();
    }

    public void DisplaySnapshot(EngineObjectHeapSnapshot snapshot, string source = "External") =>
        SetSnapshot(snapshot, source);

    protected override void OnEnable()
    {
        minSize = new Vector2(500, 300);
        titleContent = new GUIContent("Memory Profiler", "Icons/Windows/Window.png",
            "Capture engine objects and their managed references");
    }

    protected override void OnGUI()
    {
        DrawToolbar();
        if (_snapshot is null)
        {
            GUILayout.Space(12);
            GUILayout.Label(_status, EditorStyles.wordWrappedLabel);
            return;
        }

        var snapshot = _snapshot;
        GUILayout.Space(5);
        GUILayout.Label(
            $"{_source} · {snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss} · " +
            $"{snapshot.Objects.Length:N0} objects · {snapshot.References.Length:N0} references · " +
            $"managed {FormatBytes(snapshot.ManagedHeapBytes)}",
            EditorStyles.miniBoldLabel);
        if (_baseline is not null)
            GUILayout.Label(
                $"Compared with baseline: objects {Signed(snapshot.Objects.Length - _baseline.Objects.Length)}, " +
                $"managed {SignedBytes(snapshot.ManagedHeapBytes - _baseline.ManagedHeapBytes)}",
                EditorStyles.miniLabel);
        GUILayout.Label($"Known native allocations: {snapshot.NativeAllocations.Length:N0} · {FormatBytes(_view!.NativeBytes)} (requested sizes)", EditorStyles.miniLabel);
        GUILayout.Label($"Streaming texture payload: {FormatBytes(BEngine.Rendering.TextureStreaming.residentMemoryBytes)}", EditorStyles.miniLabel);
        if (snapshot.NativeAllocations.Length > 0)
        {
            _showNativeAllocations = EditorGUILayout.Foldout(_showNativeAllocations, "Native allocation details");
            if (_showNativeAllocations)
                foreach (var allocation in snapshot.NativeAllocations.Take(12))
                    GUILayout.Label($"{allocation.Category} · {allocation.Label} · {FormatBytes(allocation.Bytes)}", EditorStyles.miniLabel);
        }
        // GUILayout groups reserve their height immediately; an unsized horizontal
        // group clips its children to one control row rather than fitting them later.
        var bodyHeight = Fix64.Max(80, GUIUtility.currentViewHeight - GUILayoutUtility.GetLastRect().yMax - 34);
        var bodyWidth = Fix64.Max(2, GUIUtility.currentViewWidth - 20);
        var listWidth = bodyWidth * 55 / 100;
        GUILayout.BeginHorizontal(GUILayout.Height(bodyHeight));
        DrawObjectList(snapshot, listWidth);
        DrawDetails(snapshot, bodyWidth - listWidth);
        GUILayout.EndHorizontal();
        GUILayout.Label(_status, EditorStyles.miniLabel);
    }

    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(24));
        if (GUILayout.Button("CLR Heap", EditorStyles.toolbarButton, GUILayout.Width(70))) GetWindow<BEngine.Editor.Diagnostics.ManagedMemoryProfilerWindow>("CLR Heap").Show();
        if (GUILayout.Button("Capture Editor", EditorStyles.toolbarButton, GUILayout.Width(94)))
            SetSnapshot(EngineObjectHeapSnapshot.Capture(), "Editor");
        using (new EditorGUI.DisabledScope(_snapshot is null))
        {
            if (GUILayout.Button("Set Baseline", EditorStyles.toolbarButton, GUILayout.Width(82)))
                _baseline = _snapshot;
            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(45))) Save();
        }
        if (GUILayout.Button("Load", EditorStyles.toolbarButton, GUILayout.Width(45))) Load();
        GUILayout.FlexibleSpace();
        GUILayout.Label("Search", EditorStyles.toolbarLabel, GUILayout.Width(45));
        _search = GUILayout.TextField(_search, GUILayout.Width(180));
        GUILayout.EndHorizontal();
    }

    private void DrawObjectList(EngineObjectHeapSnapshot snapshot, Fix64 width)
    {
        GUILayout.BeginVertical(GUILayout.Width(width), GUILayout.ExpandHeight(true));
        GUILayout.Label("Objects", EditorStyles.boldLabel);
        var visible = _view!.GetRows(_search);
        if (!ReferenceEquals(_visibleRows, visible)) { _visibleRows = visible; _page = 0; _listScroll = default; }
        var pages = Math.Max(1, (visible.Length + PageSize - 1) / PageSize);
        GUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(_page == 0))
            if (GUILayout.Button("Previous", EditorStyles.miniButton, GUILayout.Width(60))) { _page--; _listScroll = default; }
        GUILayout.Label($"Page {_page + 1:N0}/{pages:N0} · {visible.Length:N0} matches", GUILayout.Width(Fix64.Max(1, width - 124)));
        using (new EditorGUI.DisabledScope(_page + 1 >= pages))
            if (GUILayout.Button("Next", EditorStyles.miniButton, GUILayout.Width(40))) { _page++; _listScroll = default; }
        GUILayout.EndHorizontal();
        _listScroll = _listView.Begin(_listScroll);
        for (var index = _page * PageSize; index < Math.Min(visible.Length, (_page + 1) * PageSize); index++)
        {
            var node = visible[index];
            var label = $"{ShortType(node.TypeName)}  {DisplayName(node)}  {FormatBytes(node.EstimatedShallowBytes)}";
            if (GUILayout.Button(label, node.SnapshotId == _selectedSnapshotId
                    ? EditorStyles.toolbarIconButtonSelected
                    : EditorStyles.miniButton))
                _selectedSnapshotId = node.SnapshotId;
        }
        _listView.End();
        GUILayout.EndVertical();
    }

    private void DrawDetails(EngineObjectHeapSnapshot snapshot, Fix64 width)
    {
        GUILayout.BeginVertical(GUILayout.Width(width), GUILayout.ExpandHeight(true));
        GUILayout.Label("References", EditorStyles.boldLabel);
        _detailsScroll = _detailsView.Begin(_detailsScroll);
        var node = _view!.FindObject(_selectedSnapshotId);
        if (node is null)
            GUILayout.Label("Select an object to inspect incoming and outgoing references.",
                EditorStyles.wordWrappedLabel);
        else
        {
            GUILayout.Label(node.TypeName, EditorStyles.boldLabel);
            GUILayout.Label($"Instance {node.InstanceId} · {DisplayName(node)} · {FormatBytes(node.EstimatedShallowBytes)}",
                EditorStyles.wordWrappedMiniLabel);
            if (BObject.FindObjectFromInstanceID(node.InstanceId) is { } local &&
                GUILayout.Button("Ping Object")) EditorGUIUtility.PingObject(local);
            DrawReferences(snapshot, node, outgoing: true);
            DrawReferences(snapshot, node, outgoing: false);
        }
        _detailsView.End();
        GUILayout.EndVertical();
    }

    private void DrawReferences(
        EngineObjectHeapSnapshot snapshot,
        EngineObjectHeapNode node,
        bool outgoing)
    {
        GUILayout.Space(6);
        GUILayout.Label(outgoing ? "Outgoing" : "Incoming", EditorStyles.boldLabel);
        var references = _view!.GetReferences(node.SnapshotId, outgoing);
        if (references.Length == 0) GUILayout.Label("None", EditorStyles.miniLabel);
        foreach (var reference in references)
        {
            var otherId = outgoing ? reference.TargetSnapshotId : reference.SourceSnapshotId;
            var other = _view.FindObject(otherId);
            GUILayout.Label($"{reference.Path} → {(other is null ? "Missing" : ShortType(other.TypeName) + " " + DisplayName(other))}",
                EditorStyles.wordWrappedMiniLabel);
        }
    }

    private void SetSnapshot(EngineObjectHeapSnapshot snapshot, string source)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _view = new MemoryProfilerSnapshotView(snapshot);
        _snapshot = snapshot = _view.Snapshot;
        _source = source;
        _selectedSnapshotId = snapshot.Objects.FirstOrDefault()?.SnapshotId ?? 0;
        _status = $"Captured {snapshot.Objects.Length:N0} engine objects.";
        Repaint();
    }

    private void Save()
    {
        if (_snapshot is null) return;
        var projectPath = EditorApplication.projectPath;
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            _status = "Open a project before saving a memory snapshot.";
            return;
        }
        var root = Path.Combine(Path.GetFullPath(projectPath), "Library", "Diagnostics", "Snapshots");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, $"heap-{DateTime.UtcNow:yyyyMMdd-HHmmss}.bheap.json");
        File.WriteAllText(path, JsonSerializer.Serialize(_snapshot, JsonOptions));
        _status = "Saved " + path;
    }

    private void Load()
    {
        var path = EditorUtility.OpenFilePanel("Open memory snapshot", string.Empty, "json");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var snapshot = JsonSerializer.Deserialize<EngineObjectHeapSnapshot>(
                File.ReadAllText(path), JsonOptions) ??
                           throw new InvalidDataException("The snapshot file is empty.");
            SetSnapshot(snapshot, Path.GetFileName(path));
            _status = "Loaded " + path;
        }
        catch (Exception exception) { _status = "Load failed: " + exception.Message; }
    }

    private static string DisplayName(EngineObjectHeapNode node) =>
        string.IsNullOrWhiteSpace(node.Name) ? $"#{node.InstanceId}" : node.Name;

    private static string ShortType(string typeName) =>
        typeName[(typeName.LastIndexOf('.') + 1)..];

    private static string Signed(int value) => value >= 0 ? $"+{value:N0}" : value.ToString("N0");

    private static string SignedBytes(long value) => value >= 0
        ? "+" + FormatBytes(value)
        : "-" + FormatBytes(-value);

    private static string FormatBytes(long bytes)
    {
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < ByteUnits.Length - 1) { value /= 1024; unit++; }
        return $"{value:F1} {ByteUnits[unit]}";
    }
}
