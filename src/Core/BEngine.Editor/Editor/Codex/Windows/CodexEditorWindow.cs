using BEngine.Editor;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace BEngine.Editor.Codex;

[EditorWindowIcon("Icons/Windows/Codex.png")]
internal sealed class CodexEditorWindow : EditorWindow
{
    private static readonly string[] EffortOptions = ["low", "medium", "high", "xhigh"];
    private static readonly string[] ApprovalOptions = ["untrusted", "on-request", "never"];
    private static readonly string[] ApprovalLabels = ["Untrusted", "On Request", "Never"];
    private readonly List<string> _contextPaths = [];
    private CodexAppServerClient? _client;
    private string _prompt = string.Empty;
    private bool _showActivity = true;
    private bool _showSettings;
    private Task? _operation;
    private int _repaintQueued;
    [MenuItem("Window/Codex", false, 300)]
    private static void ShowCodexWindow() => GetWindow<CodexEditorWindow>("Codex");

    protected override void OnEnable()
    {
        titleContent = new GUIContent("Codex", "Icons/Windows/Codex.png", "Codex coding agent");
        minSize = new Vector2(420, 340); position = new Rect(240, 90, 660, 720); CreateClient();
    }
    protected override void OnDisable()
    {
        if (_client is { } client) client.changed -= OnClientChanged;
        _client?.Dispose();
        _client = null;
        Interlocked.Exchange(ref _repaintQueued, 0);
    }
    protected override void OnGUI()
    {
        if (_client is null) { GUILayout.Label("Codex unavailable."); return; }
        var snapshot = _client.GetSnapshot();
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Codex | {Path.GetFileName(_client.ProjectRoot)}", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.New, "New conversation", GUILayout.Width(26)))
            RunOperation(() => _client.NewConversationAsync());
        _showSettings = EditorToolbar.IconToggle(_showSettings, EditorBuiltinIcons.Toolbar.Settings,
            "Codex settings", GUILayout.Width(26));
        GUILayout.EndHorizontal();
        GUILayout.Label(snapshot.StatusText, EditorStyles.miniLabel);
        if (snapshot.StatusText == "Sign in required" || !string.IsNullOrWhiteSpace(snapshot.LoginUrl))
        {
            EditorGUILayout.HelpBox("Sign in to Codex before sending a message.", MessageType.Info);
            if (string.IsNullOrWhiteSpace(snapshot.LoginUrl))
            {
                if (GUILayout.Button("Sign in with ChatGPT"))
                    RunOperation(BeginLoginAsync);
            }
            else if (GUILayout.Button("Open sign-in page"))
            {
                Application.OpenURL(snapshot.LoginUrl);
            }
        }
        GUILayout.BeginHorizontal();
        var selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (GUILayout.Button(new GUIContent("Selection", EditorBuiltinIcons.Toolbar.Add,
                "Add selected asset to context")) && !string.IsNullOrWhiteSpace(selectedPath) &&
            !_contextPaths.Contains(selectedPath, StringComparer.OrdinalIgnoreCase)) _contextPaths.Add(selectedPath);
        _showActivity = EditorToolbar.IconToggle(_showActivity, EditorBuiltinIcons.Toolbar.Visible,
            "Show activity", GUILayout.Width(26));
        GUILayout.EndHorizontal();
        foreach (var entry in snapshot.Transcript.TakeLast(100))
        {
            if (!_showActivity && entry.Kind is CodexTranscriptKind.Activity or CodexTranscriptKind.Plan) continue;
            GUILayout.Label($"{entry.Kind}: {entry.Text}");
        }
        if (snapshot.Approvals.FirstOrDefault() is { } approval)
        {
            EditorGUILayout.HelpBox(approval.Reason, MessageType.Warning);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Allow")) RunOperation(() => _client.RespondToApprovalAsync(approval, "accept"));
            if (GUILayout.Button("Decline")) RunOperation(() => _client.RespondToApprovalAsync(approval, "decline"));
            GUILayout.EndHorizontal();
        }
        GUILayout.FlexibleSpace();
        _prompt = EditorGUILayout.TextArea(_prompt, GUILayout.Height(80));
        if (snapshot.IsTurnRunning)
        {
            if (GUILayout.Button(new GUIContent("Stop", EditorBuiltinIcons.Toolbar.Stop, "Stop request")))
                RunOperation(() => _client.InterruptAsync());
        }
        else if (GUILayout.Button(new GUIContent("Send", EditorBuiltinIcons.Toolbar.Send, "Send prompt"))) SendPrompt();
        if (_showSettings) DrawSettings();
    }
    private void DrawSettings()
    {
        var settings = _client!.Settings;
        GUILayout.Box("Codex Settings");
        settings.ExecutablePath = EditorGUILayout.TextField("Executable", settings.ExecutablePath);
        settings.Model = EditorGUILayout.TextField("Model", settings.Model);
        var effort = Array.IndexOf(EffortOptions, settings.Effort);
        settings.Effort = EffortOptions[EditorGUILayout.Popup("Effort", Math.Max(0, effort), EffortOptions)];
        var approval = Array.IndexOf(ApprovalOptions, settings.ApprovalPolicy);
        settings.ApprovalPolicy = ApprovalOptions[EditorGUILayout.Popup("Approval", Math.Max(0, approval), ApprovalLabels)];
        settings.NetworkAccess = EditorGUILayout.Toggle("Network", settings.NetworkAccess);
        if (GUILayout.Button(new GUIContent("Save and Restart", EditorBuiltinIcons.Toolbar.Save,
                "Save settings and restart Codex")))
        { _client.SaveSettings(); _showSettings = false; RestartClient(); }
    }
    private void SendPrompt()
    {
        var prompt = _prompt.Trim(); if (prompt.Length == 0 || _client is null) return;
        if (RunOperation(() => _client.SendTurnAsync(prompt, _contextPaths.ToArray()))) _prompt = string.Empty;
    }
    private void CreateClient()
    {
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
        _client = new CodexAppServerClient(projectRoot); _client.changed += OnClientChanged;
        RunOperation(() => _client.StartAsync());
    }
    private void RestartClient()
    {
        if (_client is { } client) client.changed -= OnClientChanged;
        _client?.Dispose(); _operation = null; CreateClient();
    }
    private async Task BeginLoginAsync()
    {
        if (_client is null) return;
        var url = await _client.BeginChatGptLoginAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(url))
            QueueMainThread(() => Application.OpenURL(url), "Open Codex sign-in page");
    }
    private bool RunOperation(Func<Task> operation)
    {
        if (_operation is { IsCompleted: false }) return false;
        _operation = ObserveAsync(operation);
        return true;
    }
    private async Task ObserveAsync(Func<Task> operation)
    {
        try { await operation().ConfigureAwait(false); }
        catch (Exception exception)
        {
            QueueMainThread(() => EditorFeatureGuard.Report("Codex operation", exception),
                "Report Codex operation failure");
        }
    }

    private void OnClientChanged()
    {
        var scheduler = EditorApplication.TaskScheduler;
        if (scheduler is null) return;
        if (scheduler.IsMainThread)
        {
            Repaint();
            return;
        }
        if (Interlocked.Exchange(ref _repaintQueued, 1) != 0) return;
        QueueMainThread(() =>
        {
            Interlocked.Exchange(ref _repaintQueued, 0);
            if (_client is not null) Repaint();
        }, "Repaint Codex window");
    }

    private static void QueueMainThread(Action callback, string name)
    {
        var scheduler = EditorApplication.TaskScheduler;
        if (scheduler is null) return;
        if (scheduler.IsMainThread)
        {
            callback();
            return;
        }
        try { scheduler.Post(callback, name); }
        catch (ObjectDisposedException) { }
    }
}
