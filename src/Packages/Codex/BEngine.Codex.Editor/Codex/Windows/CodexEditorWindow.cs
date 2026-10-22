using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.Editor.Codex;

[EditorWindowIcon("Icons/Windows/Codex.png")]
public sealed partial class CodexEditorWindow :
    EditorWindow<CodexEditorWindowModel, CodexEditorWindowController>
{
    private static readonly string[] EffortOptions = ["low", "medium", "high", "xhigh"];
    private List<string> _contextPaths => model.ContextPaths;
    private CodexAppServerClient? _client => model.Client;
    private string _prompt { get => model.Prompt; set => model.Prompt = value; }
    private bool _showActivity { get => model.ShowActivity; set => model.ShowActivity = value; }

    protected override CodexEditorWindowModel CreateWindowModel(EditorWindowMvcContext context) =>
        new(context.WindowId);

    protected override CodexEditorWindowController CreateWindowController(
        EditorWindowMvcContext context,
        CodexEditorWindowModel windowModel) => new(
        context,
        windowModel,
        EditorServiceAccess.GetRequiredService<PathUtility>(),
        EditorServiceAccess.GetRequiredService<PlatformUtility>(),
        EditorServiceAccess.GetRequiredService<ProcessHostUtility>());
    [MenuItem("Window/Codex", false, 300)]
    private static void ShowCodexWindow() => GetWindow<CodexEditorWindow>("Codex");

    protected override void OnEnable()
    {
        titleContent = new GUIContent("Codex", "Icons/Windows/Codex.png", "Codex coding agent");
        minSize = new Vector2(420, 420); position = new Rect(180, 70, 1040, 780);
        controller.Activate();
    }
    protected override void OnDisable()
    {
        controller.Deactivate();
    }
    protected override void OnGUI()
    {
        DrawWorkspace();
    }
}

public sealed class CodexEditorWindowModel(string windowId) : EditorWindowModel(windowId)
{
    internal List<string> ContextPaths { get; } = [];
    public IReadOnlyList<string> ContextAssetPaths => ContextPaths;
    internal CodexAppServerClient? Client { get; set; }
    public CodexClientSnapshot? Snapshot => Client?.GetSnapshot();
    public string Prompt { get; internal set; } = string.Empty;
    public bool ShowActivity { get; internal set; }
    public bool ShowSettings { get; internal set; }
    public bool ShowSidebar { get; internal set; } = true;
    public Vector2 ConversationScroll { get; internal set; }
    public Vector2 HistoryScroll { get; internal set; }
    public Vector2 RequestScroll { get; internal set; }
    public bool FollowOutput { get; internal set; } = true;
    public string HistorySearch { get; internal set; } = string.Empty;
    internal HashSet<string> ExpandedEntries { get; } = [];
    internal Dictionary<string, string> Answers { get; } = [];
    internal Dictionary<string, (string Prompt, string[] Context)> Drafts { get; } = [];
    internal string RenameThreadId { get; set; } = string.Empty;
    internal string RenameTitle { get; set; } = string.Empty;
    public bool IsOperationRunning => Operation is { IsCompleted: false };
    internal Task? Operation { get; set; }
}

public sealed class CodexEditorWindowController :
    EditorWindowController<CodexEditorWindow, CodexEditorWindowModel>
{
    private readonly PathUtility _pathService;
    private readonly PlatformUtility _platformService;
    private readonly ProcessHostUtility _processHost;
    private int _repaintQueued;

    public CodexEditorWindowController(
        EditorWindowMvcContext context,
        CodexEditorWindowModel model,
        PathUtility pathService,
        PlatformUtility platformService,
        ProcessHostUtility processHost) : base(context, model)
    {
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        _processHost = processHost ?? throw new ArgumentNullException(nameof(processHost));
    }

    public string GetFileName(string path) => _pathService.GetFileName(path);

    public void Activate() => CreateClient();

    public void Deactivate()
    {
        if (Model.Client is { } client) client.changed -= OnClientChanged;
        Model.Client?.Dispose();
        Model.Client = null;
        Model.Operation = null;
        Interlocked.Exchange(ref _repaintQueued, 0);
    }

    public void AddContextPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            Model.ContextPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        Model.ContextPaths.Add(path);
        Repaint();
    }

    public void NewConversation()
    {
        if (Model.Client is { } client) SwitchConversation(() => client.NewConversationAsync());
    }

    public void ResumeConversation(string id)
    {
        if (Model.Client is { } client) SwitchConversation(() => client.ResumeThreadAsync(id));
    }

    public void RefreshConversations(bool archived, bool loadMore = false)
    {
        if (Model.Client is { } client) RunOperation(() => client.RefreshThreadsAsync(archived, loadMore));
    }

    public void RenameConversation(string id, string title)
    {
        if (Model.Client is { } client && !string.IsNullOrWhiteSpace(title))
            RunOperation(() => client.RenameThreadAsync(id, title));
    }

    public void ArchiveConversation(string id, bool archive)
    {
        if (Model.Client is { } client) SwitchConversation(() => client.ArchiveThreadAsync(id, archive));
    }

    public void RemoveContextPath(string path) { Model.ContextPaths.Remove(path); Repaint(); }

    public void AnswerQuestions(CodexUserInputRequest request)
    {
        var answers = request.Questions.ToDictionary(question => question.Id,
            question => Model.Answers.GetValueOrDefault(request.RequestIdJson + ":" + question.Id, string.Empty));
        if (Model.Client is { } client) RunOperation(() => client.AnswerQuestionsAsync(request, answers));
    }

    private void SwitchConversation(Func<Task> operation)
    {
        if (Model.IsOperationRunning) return;
        var current = Model.Snapshot?.ThreadId ?? string.Empty;
        Model.Drafts[current] = (Model.Prompt, Model.ContextPaths.ToArray());
        RunOperation(async () =>
        {
            await operation().ConfigureAwait(false);
            QueueMainThread(() =>
            {
                var key = Model.Snapshot?.ThreadId ?? string.Empty;
                var draft = Model.Drafts.GetValueOrDefault(key, (string.Empty, Array.Empty<string>()));
                Model.Prompt = draft.Item1;
                Model.ContextPaths.Clear(); Model.ContextPaths.AddRange(draft.Item2);
                Model.ConversationScroll = Vector2.zero; Model.FollowOutput = true;
                Model.ExpandedEntries.Clear(); Model.Answers.Clear();
                Model.ShowSettings = false;
                Model.RenameThreadId = string.Empty;
                Repaint();
            }, "Switch Codex conversation");
        });
    }

    public void BeginLogin() => RunOperation(BeginLoginAsync);

    public void RespondToApproval(CodexApprovalRequest approval, bool accept)
    {
        ArgumentNullException.ThrowIfNull(approval);
        if (Model.Client is { } client)
            RunOperation(() => client.RespondToApprovalAsync(
                approval, accept ? "accept" : "decline"));
    }

    public void Interrupt()
    {
        if (Model.Client is { } client) RunOperation(() => client.InterruptAsync());
    }

    public bool SendPrompt()
    {
        var prompt = Model.Prompt.Trim();
        if (prompt.Length == 0 || Model.IsOperationRunning || Model.Client is not { } client) return false;
        var context = Model.ContextPaths.ToArray();
        var followUp = client.GetSnapshot().IsTurnRunning;
        Model.Prompt = string.Empty;
        Model.ContextPaths.Clear();
        if (!RunOperation(async () =>
            {
                try
                {
                    if (followUp) await client.SteerTurnAsync(prompt, context).ConfigureAwait(false);
                    else await client.SendTurnAsync(prompt, context).ConfigureAwait(false);
                }
                catch
                {
                    QueueMainThread(() =>
                    {
                        if (Model.Prompt.Length == 0) Model.Prompt = prompt;
                        foreach (var path in context) AddContextPath(path);
                        Repaint();
                    }, "Restore unsent Codex draft");
                    throw;
                }
            }))
        {
            Model.Prompt = prompt;
            Model.ContextPaths.AddRange(context);
            return false;
        }
        Model.FollowOutput = true;
        Repaint();
        return true;
    }

    public void SaveSettingsAndRestart()
    {
        Model.Client?.SaveSettings();
        Model.ShowSettings = false;
        RestartClient();
    }

    private void CreateClient()
    {
        var projectRoot = EditorApplication.projectPath;
        if (string.IsNullOrWhiteSpace(projectRoot))
            projectRoot = _pathService.GetDirectoryName(Application.dataPath) ?? _platformService.CurrentDirectory;
        var client = new CodexAppServerClient(projectRoot, _processHost);
        Model.Client = client;
        client.changed += OnClientChanged;
        RunOperation(() => client.StartAsync());
    }

    private void RestartClient()
    {
        if (Model.Client is { } client) client.changed -= OnClientChanged;
        Model.Client?.Dispose();
        Model.Client = null;
        Model.Operation = null;
        CreateClient();
    }

    private async Task BeginLoginAsync()
    {
        if (Model.Client is not { } client) return;
        var url = await client.BeginChatGptLoginAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(url))
            QueueMainThread(() => Application.OpenURL(url), "Open Codex sign-in page");
    }

    private bool RunOperation(Func<Task> operation)
    {
        if (Model.IsOperationRunning) return false;
        Model.Operation = ObserveAsync(operation);
        return true;
    }

    private static async Task ObserveAsync(Func<Task> operation)
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
            if (Model.Client is not null) Repaint();
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
