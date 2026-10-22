using System.Text.Json;
using BEngine.Diagnostics;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/Window.png")]
public sealed class MemoryProfilerWindow : EditorWindow
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { WriteIndented = true };
    private readonly EditorScrollView _listView = new();
    private readonly EditorScrollView _detailsView = new();
    private EngineObjectHeapSnapshot? _snapshot;
    private EngineObjectHeapSnapshot? _baseline;
    private string _source = "Editor";
    private string _search = string.Empty;
    private string _status = "Capture the Editor heap or capture a connected Player from Remote Player Profiler.";
    private int _selectedSnapshotId;
    private Vector2 _listScroll;
    private Vector2 _detailsScroll;

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
        GUILayout.Label($"Known native allocations: {snapshot.NativeAllocations.Length:N0} · {FormatBytes(snapshot.NativeAllocations.Sum(item => item.Bytes))} (requested sizes)", EditorStyles.miniLabel);
        GUILayout.Label($"Streaming texture payload: {FormatBytes(BEngine.Rendering.TextureStreaming.residentMemoryBytes)}", EditorStyles.miniLabel);
        foreach (var allocation in snapshot.NativeAllocations.Take(12)) GUILayout.Label($"{allocation.Category} · {allocation.Label} · {FormatBytes(allocation.Bytes)}", EditorStyles.miniLabel);
        GUILayout.BeginHorizontal();
        DrawObjectList(snapshot);
        DrawDetails(snapshot);
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

    private void DrawObjectList(EngineObjectHeapSnapshot snapshot)
    {
        GUILayout.BeginVertical(GUILayout.MinWidth(280), GUILayout.ExpandHeight(true));
        GUILayout.Label("Objects", EditorStyles.boldLabel);
        var visible = snapshot.Objects.Where(Matches).Take(10_000).ToArray();
        _listScroll = _listView.Begin(_listScroll);
        foreach (var node in visible)
        {
            var label = $"{ShortType(node.TypeName)}  {DisplayName(node)}  {FormatBytes(node.EstimatedShallowBytes)}";
            if (GUILayout.Button(label, node.SnapshotId == _selectedSnapshotId
                    ? EditorStyles.toolbarIconButtonSelected
                    : EditorStyles.miniButton))
                _selectedSnapshotId = node.SnapshotId;
        }
        _listView.End();
        GUILayout.EndVertical();
    }

    private void DrawDetails(EngineObjectHeapSnapshot snapshot)
    {
        GUILayout.BeginVertical(GUILayout.MinWidth(210), GUILayout.ExpandHeight(true));
        GUILayout.Label("References", EditorStyles.boldLabel);
        _detailsScroll = _detailsView.Begin(_detailsScroll);
        var node = snapshot.Objects.FirstOrDefault(item => item.SnapshotId == _selectedSnapshotId);
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

    private static void DrawReferences(
        EngineObjectHeapSnapshot snapshot,
        EngineObjectHeapNode node,
        bool outgoing)
    {
        GUILayout.Space(6);
        GUILayout.Label(outgoing ? "Outgoing" : "Incoming", EditorStyles.boldLabel);
        var references = snapshot.References.Where(reference => outgoing
            ? reference.SourceSnapshotId == node.SnapshotId
            : reference.TargetSnapshotId == node.SnapshotId).Take(1000).ToArray();
        if (references.Length == 0) GUILayout.Label("None", EditorStyles.miniLabel);
        foreach (var reference in references)
        {
            var otherId = outgoing ? reference.TargetSnapshotId : reference.SourceSnapshotId;
            var other = snapshot.Objects.FirstOrDefault(item => item.SnapshotId == otherId);
            GUILayout.Label($"{reference.Path} → {(other is null ? "Missing" : ShortType(other.TypeName) + " " + DisplayName(other))}",
                EditorStyles.wordWrappedMiniLabel);
        }
    }

    private void SetSnapshot(EngineObjectHeapSnapshot snapshot, string source)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _source = source;
        _selectedSnapshotId = snapshot.Objects.FirstOrDefault()?.SnapshotId ?? 0;
        _status = $"Captured {snapshot.Objects.Length:N0} engine objects.";
        Repaint();
    }

    private void Save()
    {
        if (_snapshot is null) return;
        var root = string.IsNullOrWhiteSpace(EditorApplication.projectPath)
            ? Path.GetTempPath()
            : Path.Combine(EditorApplication.projectPath, "Temp", "Diagnostics");
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

    private bool Matches(EngineObjectHeapNode node) => string.IsNullOrWhiteSpace(_search) ||
        node.TypeName.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
        node.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
        node.InstanceId.ToString().Contains(_search, StringComparison.Ordinal);

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
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:F1} {units[unit]}";
    }
}
