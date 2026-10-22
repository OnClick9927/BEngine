using BEngine.Diagnostics;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/Window.png")]
public sealed class RemotePlayerProfilerWindow : EditorWindow
{
    private const int MaximumSamples = 240;
    private readonly List<RuntimeDiagnosticsSample> _samples = [];
    private readonly EditorScrollView _sampleScrollView = new();
    private string _host = "127.0.0.1";
    private int _port = 54991;
    private string _token = string.Empty;
    private string _status = "Disconnected";
    private RemoteDiagnosticsClient? _client;
    private Task<RemoteDiagnosticsClient>? _connectTask;
    private Task<RuntimeDiagnosticsSample>? _sampleTask;
    private Task<EngineObjectHeapSnapshot>? _heapTask;
    private Vector2 _sampleScroll;
    private double _nextPoll;

    [MenuItem("Window/Analysis/Remote Player Profiler", false, 212)]
    private static void Open() => GetWindow<RemotePlayerProfilerWindow>("Remote Player");

    protected override void OnEnable()
    {
        minSize = new Vector2(430, 280);
        titleContent = new GUIContent("Remote Player", "Icons/Windows/Window.png",
            "Profile a running BEngine Player over an authenticated diagnostics connection");
    }

    protected override void OnDisable() => Disconnect();

    protected override void Update()
    {
        CompleteConnect();
        CompleteSample();
        CompleteHeap();
        if (_client is not null && _sampleTask is null &&
            EditorApplication.timeSinceStartup >= _nextPoll)
        {
            _nextPoll = EditorApplication.timeSinceStartup + 0.25;
            _sampleTask = _client.CaptureSampleAsync();
        }
        Repaint();
    }

    protected override void OnGUI()
    {
        DrawToolbar();
        GUILayout.Space(6);
        GUILayout.Label(_status, _client is null ? EditorStyles.miniLabel : EditorStyles.miniBoldLabel);
        if (_samples.Count == 0)
        {
            GUILayout.Space(12);
            GUILayout.Label("Start a Player with --diagnostics-port and connect to inspect live CPU, GPU and memory samples.",
                EditorStyles.wordWrappedLabel);
            return;
        }

        var latest = _samples[^1];
        GUILayout.BeginHorizontal();
        DrawMetric("CPU", $"{latest.ProcessCpuPercent:F1}%");
        DrawMetric("GPU", latest.GpuFrame.Sequence > 0
            ? $"{latest.GpuFrame.Milliseconds:F3} ms"
            : "Pending");
        DrawMetric("Managed", FormatBytes(latest.ManagedHeapBytes));
        DrawMetric("Objects", latest.EngineObjectCount.ToString("N0"));
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
        GUILayout.Label("Recent samples", EditorStyles.boldLabel);
        _sampleScroll = _sampleScrollView.Begin(_sampleScroll);
        foreach (var sample in _samples.TakeLast(80).Reverse())
            GUILayout.Label(
                $"{sample.CapturedAtUtc:HH:mm:ss.fff}   CPU {sample.ProcessCpuPercent,5:F1}%   " +
                $"GPU {(sample.GpuFrame.Sequence > 0 ? sample.GpuFrame.Milliseconds.ToString("F3") : "-")} ms   " +
                $"Heap {FormatBytes(sample.ManagedHeapBytes),10}   Objects {sample.EngineObjectCount:N0}",
                EditorStyles.miniLabel);
        _sampleScrollView.End();
    }

    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(24));
        using (new EditorGUI.DisabledScope(_client is not null || _connectTask is not null))
        {
            _host = GUILayout.TextField(_host, GUILayout.Width(120));
            var portText = GUILayout.TextField(_port.ToString(), GUILayout.Width(58));
            if (int.TryParse(portText, out var port)) _port = Math.Clamp(port, 1, 65535);
            _token = GUILayout.PasswordField(_token, '*', -1, (GUIStyle?)null, GUILayout.Width(110));
        }
        if (_client is null)
        {
            using (new EditorGUI.DisabledScope(_connectTask is not null))
                if (GUILayout.Button("Connect", EditorStyles.toolbarButton, GUILayout.Width(68))) Connect();
        }
        else if (GUILayout.Button("Disconnect", EditorStyles.toolbarButton, GUILayout.Width(78)))
            Disconnect();
        if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(48))) _samples.Clear();
        using (new EditorGUI.DisabledScope(_client is null || _heapTask is not null))
            if (GUILayout.Button("Capture Heap", EditorStyles.toolbarButton, GUILayout.Width(88)))
                _heapTask = _client!.CaptureHeapAsync();
        GUILayout.EndHorizontal();
    }

    private void Connect()
    {
        Disconnect();
        _status = $"Connecting to {_host}:{_port}...";
        _connectTask = RemoteDiagnosticsClient.ConnectAsync(_host, _port, _token);
    }

    private void CompleteConnect()
    {
        if (_connectTask is not { IsCompleted: true } task) return;
        _connectTask = null;
        try
        {
            _client = task.GetAwaiter().GetResult();
            _status = $"Connected to PID {_client.Hello.ProcessId} on {_client.Hello.MachineName}";
            _nextPoll = 0;
        }
        catch (Exception exception) { _status = "Connection failed: " + exception.Message; }
    }

    private void CompleteSample()
    {
        if (_sampleTask is not { IsCompleted: true } task) return;
        _sampleTask = null;
        try
        {
            _samples.Add(task.GetAwaiter().GetResult());
            while (_samples.Count > MaximumSamples) _samples.RemoveAt(0);
        }
        catch (Exception exception)
        {
            _status = "Sampling failed: " + exception.Message;
            DisconnectClient();
        }
    }

    private void CompleteHeap()
    {
        if (_heapTask is not { IsCompleted: true } task) return;
        _heapTask = null;
        try { MemoryProfilerWindow.ShowSnapshot(task.GetAwaiter().GetResult(), "Remote Player"); }
        catch (Exception exception) { _status = "Heap capture failed: " + exception.Message; }
    }

    private void Disconnect()
    {
        _connectTask = null;
        _sampleTask = null;
        _heapTask = null;
        DisconnectClient();
    }

    private void DisconnectClient()
    {
        if (_client is not null) _client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _client = null;
        if (!_status.StartsWith("Connection failed", StringComparison.Ordinal) &&
            !_status.StartsWith("Sampling failed", StringComparison.Ordinal))
            _status = "Disconnected";
    }

    private static void DrawMetric(string name, string value)
    {
        GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(92));
        GUILayout.Label(name, EditorStyles.miniLabel);
        GUILayout.Label(value, EditorStyles.boldLabel);
        GUILayout.EndVertical();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:F1} {units[unit]}";
    }
}
