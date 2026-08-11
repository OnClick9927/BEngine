using BEngine;
using BEngine.Editor;
using BEngine.UIElements;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using Button = BEngine.UIElements.Button;
using Label = BEngine.UIElements.Label;

namespace BEngine.Codex.Editor;

internal sealed class CodexEditorWindow : EditorWindow
{
    private static readonly string[] EffortOptions = ["low", "medium", "high", "xhigh"];
    private static readonly string[] ApprovalOptions = ["unlessTrusted", "onRequest", "never"];
    private readonly List<string> _contextPaths = [];
    private CodexAppServerClient? _client;
    private string _prompt = string.Empty;
    private bool _showActivity = true;
    private bool _showSettings;
    private Task? _operation;
    private long _lastRevision = -1;

    internal static bool PackageEnabled { get; set; } = true;

    [MenuItem("Window/Codex", false, 300)]
    private static void ShowCodexWindow() => GetWindow<CodexEditorWindow>("Codex");

    [MenuItem("Window/Codex", true)]
    private static bool ValidateShowCodexWindow() => PackageEnabled;

    protected override void OnEnable()
    {
        titleContent = new GUIContent("Codex", "Codex coding agent");
        minSize = new Vector2(420, 340);
        position = new Rect(240, 90, 660, 720);
        CreateClient();
    }

    protected override void OnDisable()
    {
        _client?.Dispose();
        _client = null;
    }

    protected override void CreateGUI() => RebuildView();

    protected override void Update()
    {
        if (_client is null) return;
        var snapshot = _client.GetSnapshot();
        if (snapshot.Revision == _lastRevision) return;
        _lastRevision = snapshot.Revision;
        RebuildView(snapshot);
    }

    protected override void OnSelectionChange() => RebuildView();

    private void RebuildView(CodexClientSnapshot? current = null)
    {
        rootVisualElement.Clear();
        rootVisualElement.name = "CodexRoot";
        rootVisualElement.style.SetPadding(6);
        if (_client is null)
        {
            rootVisualElement.Add(new Label("Codex 不可用。"));
            return;
        }

        var snapshot = current ?? _client.GetSnapshot();
        BuildHeader(snapshot);
        BuildContextBar();
        BuildTranscript(snapshot);
        if (snapshot.Approvals.FirstOrDefault() is { } approval) BuildApproval(approval);
        BuildComposer(snapshot);
        if (_showSettings) BuildSettings();
    }

    private void BuildHeader(CodexClientSnapshot snapshot)
    {
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        var status = new Label("o");
        status.style.color = snapshot.ConnectionStatus switch
        {
            CodexConnectionStatus.Ready => new UIColor(82, 209, 132),
            CodexConnectionStatus.Faulted => new UIColor(242, 97, 87),
            _ => new UIColor(235, 184, 71)
        };
        header.Add(status);
        var title = new Label($"Codex  {Path.GetFileName(_client!.ProjectRoot)}");
        title.style.flexGrow = 1;
        header.Add(title);

        var create = new Button(() => RunOperation(() => _client.NewConversationAsync()), "+")
        {
            tooltip = "新建对话"
        };
        create.SetEnabled(!snapshot.IsTurnRunning && snapshot.ConnectionStatus == CodexConnectionStatus.Ready);
        header.Add(create);
        header.Add(new Button(() => { _showSettings = !_showSettings; RebuildView(); }, "设置"));
        if (snapshot.ConnectionStatus == CodexConnectionStatus.Ready && string.IsNullOrWhiteSpace(snapshot.AuthMode))
        {
            var login = new Button(() => RunOperation(LoginAsync), "登录");
            login.SetEnabled(_operation is not { IsCompleted: false });
            header.Add(login);
        }
        rootVisualElement.Add(header);
        rootVisualElement.Add(new Label(snapshot.StatusText) { tooltip = snapshot.StatusText });
    }

    private void BuildContextBar()
    {
        var bar = new Toolbar();
        bar.Add(new Label("上下文"));
        var selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
        var add = new Button(() =>
        {
            if (!string.IsNullOrWhiteSpace(selectedPath) &&
                !_contextPaths.Contains(selectedPath, StringComparer.OrdinalIgnoreCase))
                _contextPaths.Add(selectedPath);
            RebuildView();
        }, "+ 所选资源");
        add.SetEnabled(!string.IsNullOrWhiteSpace(selectedPath) &&
                       !_contextPaths.Contains(selectedPath, StringComparer.OrdinalIgnoreCase));
        bar.Add(add);
        foreach (var path in _contextPaths.ToArray())
        {
            var captured = path;
            bar.Add(new Button(() => { _contextPaths.Remove(captured); RebuildView(); }, $"{Path.GetFileName(path)} x")
            {
                tooltip = path
            });
        }
        var activity = new Toggle("活动") { value = _showActivity };
        activity.valueChanged += value => { _showActivity = value; RebuildView(); };
        bar.Add(activity);
        rootVisualElement.Add(bar);
    }

    private void BuildTranscript(CodexClientSnapshot snapshot)
    {
        var transcript = new ScrollView { name = "CodexTranscript" };
        transcript.style.minHeight = 160;
        transcript.style.height = 360;
        transcript.style.backgroundColor = new UIColor(42, 42, 42);
        transcript.style.SetPadding(6);
        if (snapshot.Transcript.Count == 0)
            transcript.Add(new Label("向 Codex 询问、解释或修改当前工程。"));
        foreach (var entry in snapshot.Transcript)
        {
            if (!_showActivity && entry.Kind is CodexTranscriptKind.Activity or CodexTranscriptKind.Plan) continue;
            var label = entry.Kind switch
            {
                CodexTranscriptKind.User => "你",
                CodexTranscriptKind.Assistant => "CODEX",
                CodexTranscriptKind.Plan => "计划",
                CodexTranscriptKind.Error => "错误",
                _ => "任务"
            };
            var heading = new Label(string.IsNullOrWhiteSpace(entry.Status) ? label : $"{label}  {entry.Status}");
            heading.style.color = entry.Kind switch
            {
                CodexTranscriptKind.User => new UIColor(97, 184, 255),
                CodexTranscriptKind.Assistant => new UIColor(89, 224, 148),
                CodexTranscriptKind.Plan => new UIColor(219, 178, 89),
                CodexTranscriptKind.Error => new UIColor(255, 102, 92),
                _ => new UIColor(166, 176, 189)
            };
            transcript.Add(heading);
            transcript.Add(new Label(string.IsNullOrWhiteSpace(entry.Text) ? "..." : entry.Text)
            {
                tooltip = "双击可复制"
            });
        }
        rootVisualElement.Add(transcript);
    }

    private void BuildApproval(CodexApprovalRequest approval)
    {
        var panel = new VisualElement();
        panel.style.backgroundColor = new UIColor(69, 58, 35);
        panel.style.SetPadding(6);
        var heading = new Label(approval.Kind == CodexApprovalKind.Command ? "命令审批" : "文件修改审批");
        heading.style.color = new UIColor(245, 194, 77);
        panel.Add(heading);
        var detail = !string.IsNullOrWhiteSpace(approval.Command) ? approval.Command : approval.Reason;
        panel.Add(new Label(string.IsNullOrWhiteSpace(detail) ? "Codex 请求权限。" : detail));
        var actions = new Toolbar();
        var allow = new Button(() => RunOperation(() => _client!.RespondToApprovalAsync(approval, "accept")), "允许");
        var session = new Button(() => RunOperation(() =>
            _client!.RespondToApprovalAsync(approval, "acceptForSession")), "本次会话允许");
        var decline = new Button(() => RunOperation(() =>
            _client!.RespondToApprovalAsync(approval, "decline")), "拒绝");
        var enabled = _operation is not { IsCompleted: false };
        allow.SetEnabled(enabled);
        session.SetEnabled(enabled);
        decline.SetEnabled(enabled);
        actions.Add(allow);
        actions.Add(session);
        actions.Add(decline);
        panel.Add(actions);
        rootVisualElement.Add(panel);
    }

    private void BuildComposer(CodexClientSnapshot snapshot)
    {
        var composer = new VisualElement();
        composer.style.flexDirection = FlexDirection.Row;
        var prompt = new TextField
        {
            value = _prompt,
            multiline = true,
            tooltip = "输入发送给 Codex 的内容"
        };
        prompt.style.width = 500;
        prompt.style.height = 84;
        prompt.valueChanged += value => _prompt = value;
        prompt.SetEnabled(snapshot.ConnectionStatus == CodexConnectionStatus.Ready && !snapshot.IsTurnRunning);
        composer.Add(prompt);
        if (snapshot.IsTurnRunning)
        {
            var stop = new Button(() => RunOperation(() => _client!.InterruptAsync()), "停止");
            stop.style.height = 80;
            composer.Add(stop);
        }
        else
        {
            var send = new Button(SendPrompt, "发送");
            send.style.height = 80;
            send.SetEnabled(snapshot.ConnectionStatus == CodexConnectionStatus.Ready &&
                            !string.IsNullOrWhiteSpace(_prompt));
            composer.Add(send);
        }
        rootVisualElement.Add(composer);
    }

    private void BuildSettings()
    {
        var settings = _client!.Settings;
        var panel = new VisualElement();
        panel.style.backgroundColor = new UIColor(62, 62, 62);
        panel.style.SetPadding(6);
        panel.Add(new Label("Codex 设置"));
        var executable = new TextField("可执行文件") { value = settings.ExecutablePath };
        executable.valueChanged += value => settings.ExecutablePath = value;
        panel.Add(executable);
        var model = new TextField("模型") { value = settings.Model };
        model.valueChanged += value => settings.Model = value;
        panel.Add(model);
        var effort = new DropdownField("推理强度", EffortOptions) { value = settings.Effort };
        effort.valueChanged += value => settings.Effort = value;
        panel.Add(effort);
        var approvals = new DropdownField("审批策略", ApprovalOptions) { value = settings.ApprovalPolicy };
        approvals.valueChanged += value => settings.ApprovalPolicy = value;
        panel.Add(approvals);
        var network = new Toggle("网络访问") { value = settings.NetworkAccess };
        network.valueChanged += value => settings.NetworkAccess = value;
        panel.Add(network);
        panel.Add(new Button(() =>
        {
            _client.SaveSettings();
            _showSettings = false;
            RestartClient();
            RebuildView();
        }, "保存并重启"));
        rootVisualElement.Add(panel);
    }

    private void SendPrompt()
    {
        var prompt = _prompt.Trim();
        if (string.IsNullOrWhiteSpace(prompt) || _client is null) return;
        var contexts = _contextPaths.ToArray();
        _prompt = string.Empty;
        RunOperation(() => _client.SendTurnAsync(prompt, contexts));
        RebuildView();
    }

    private async Task LoginAsync()
    {
        var url = await _client!.BeginChatGptLoginAsync().ConfigureAwait(false);
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private void CreateClient()
    {
        var assetsPath = Application.dataPath;
        var projectRoot = string.IsNullOrWhiteSpace(assetsPath)
            ? Directory.GetCurrentDirectory()
            : Directory.GetParent(Path.GetFullPath(assetsPath))?.FullName ?? Directory.GetCurrentDirectory();
        _client = new CodexAppServerClient(projectRoot);
        _client.changed += OnClientChanged;
        RunOperation(() => _client.StartAsync());
    }

    private void RestartClient()
    {
        if (_client is not null)
        {
            _client.changed -= OnClientChanged;
            _client.Dispose();
        }
        _operation = null;
        CreateClient();
    }

    private void OnClientChanged() => Repaint();

    private void RunOperation(Func<Task> operation)
    {
        if (_operation is { IsCompleted: false }) return;
        _operation = ObserveAsync(operation);
    }

    private static async Task ObserveAsync(Func<Task> operation)
    {
        try { await operation().ConfigureAwait(false); }
        catch (Exception exception) { Debug.LogError($"Codex: {exception.Message}"); }
    }
}
