namespace BEngine.Testing.Editor;

[EditorWindowIcon("Icons/Windows/Console.png")]
public sealed class TestRunnerWindow : EditorWindow
{
    private IReadOnlyList<TestCase> _tests = [];
    private readonly Dictionary<string, TestResult> _results = new(StringComparer.Ordinal);
    private string _selectedId = string.Empty;
    private string _search = string.Empty;
    private int _mode;
    private Vector2 _scroll;
    private bool _coverage = true;
    private CancellationTokenSource? _cancellation;
    private Task? _operation;
    private string _status = "Ready";
    private string _coveragePath = string.Empty;

    public TestRunnerWindow()
    {
        titleContent = new GUIContent("Test Runner", "Icons/Windows/Console.png");
        minSize = new Vector2(620, 360);
        position = new Rect(120, 80, 900, 620);
    }

    [MenuItem("Window/General/Test Runner", false, 220)]
    public static void Open() => GetWindow<TestRunnerWindow>("Test Runner", true).Show();

    protected override void OnEnable() => RefreshTests();

    protected override void OnDisable()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    protected override void Update()
    {
        if (_operation is not { IsCompleted: true }) return;
        _operation = null;
        Repaint();
    }

    protected override void OnGUI()
    {
        DrawToolbar();
        DrawSummary();
        DrawTests();
        DrawDetails();
    }

    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(24));
        GUI.enabled = _operation is null;
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Play, "Run visible tests")) Run(VisibleTests());
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Step, "Run selected test") &&
            _tests.FirstOrDefault(test => test.Id == _selectedId) is { } selected) Run([selected]);
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Refresh, "Refresh test discovery")) RefreshTests();
        GUI.enabled = _operation is not null;
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Stop, "Stop current test run")) _cancellation?.Cancel();
        GUI.enabled = true;
        _mode = EditorGUILayout.Popup(string.Empty, _mode, ["All", "Edit", "Play", "Player"],
            EditorStyles.toolbarPopup, GUILayout.Width(86));
        _coverage = EditorToolbar.Toggle(_coverage,
            new GUIContent("Coverage", EditorBuiltinIcons.Toolbar.Info, "Collect statement and branch coverage"),
            GUILayout.Width(92));
        _search = EditorToolbar.SearchField(_search, "Filter tests", GUILayout.ExpandWidth(true));
        GUI.enabled = CodeCoverage.Snapshot().TotalLines > 0;
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Save, "Export LCOV and JSON coverage"))
            ExportCoverage();
        GUI.enabled = true;
        GUILayout.EndHorizontal();
    }

    private void DrawSummary()
    {
        var visible = VisibleTests();
        var passed = visible.Count(test => _results.GetValueOrDefault(test.Id)?.Status == TestStatus.Passed);
        var failed = visible.Count(test => _results.GetValueOrDefault(test.Id)?.Status is TestStatus.Failed or TestStatus.TimedOut);
        GUILayout.BeginHorizontal(EditorStyles.contentToolbar, GUILayout.Height(23));
        GUILayout.Label($"{visible.Count} tests   {passed} passed   {failed} failed", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        var coverage = CodeCoverage.Snapshot();
        GUILayout.Label(coverage.TotalLines == 0 ? _status :
            $"{_status}   Lines {coverage.CoveredLines}/{coverage.TotalLines} ({coverage.LineRate:P1})   Branches {coverage.CoveredBranches}/{coverage.TotalBranches} ({coverage.BranchRate:P1})",
            EditorStyles.miniLabel);
        GUILayout.EndHorizontal();
    }

    private void DrawTests()
    {
        var tests = VisibleTests();
        var height = Fix64.Max(130, position.height * (Fix64)0.58);
        var rect = GUILayoutUtility.GetRect(300, 4096, height, height, GUILayout.ExpandWidth(true));
        GUI.Box(rect, string.Empty, EditorStyles.viewBackground);
        var viewport = new Rect(rect.x, rect.y, rect.width, rect.height);
        var contentWidth = Fix64.Max(1, viewport.width - 14);
        _scroll = GUI.BeginScrollView(viewport, _scroll,
            new Rect(0, 0, contentWidth, Fix64.Max(viewport.height, tests.Count * 22)));
        try
        {
            for (var index = 0; index < tests.Count; index++)
            {
                var test = tests[index];
                var row = new Rect(0, index * 22, contentWidth, 22);
                if (GUI.Button(row, GUIContent.none,
                        test.Id == _selectedId ? EditorStyles.treeViewRowSelected : EditorStyles.treeViewRow))
                {
                    if (test.Id == _selectedId && Event.current.clickCount > 1) TestRunnerService.OpenSource(test);
                    _selectedId = test.Id;
                }
                var result = _results.GetValueOrDefault(test.Id);
                var icon = result?.Status switch
                {
                    TestStatus.Passed => EditorBuiltinIcons.Toolbar.Check,
                    TestStatus.Failed or TestStatus.TimedOut => EditorBuiltinIcons.Toolbar.Error,
                    TestStatus.Skipped => EditorBuiltinIcons.Toolbar.Warning,
                    _ => EditorBuiltinIcons.Toolbar.Info
                };
                GUI.Label(new Rect(4, row.y + 2, 18, 18), new GUIContent(string.Empty, icon, result?.Status.ToString() ?? "Not run"));
                GUI.Label(new Rect(26, row.y, 58, row.height), test.Mode.ToString(), EditorStyles.miniLabel);
                GUI.Label(new Rect(88, row.y, Fix64.Max(80, contentWidth - 176), row.height), test.DisplayName,
                    EditorStyles.miniLabel);
                GUI.Label(new Rect(contentWidth - 84, row.y, 80, row.height),
                    result is null ? string.Empty : $"{result.DurationMilliseconds:F1} ms", EditorStyles.centeredMiniLabel);
            }
        }
        finally { GUI.EndScrollView(); }
    }

    private void DrawDetails()
    {
        var test = _tests.FirstOrDefault(item => item.Id == _selectedId);
        var result = test is null ? null : _results.GetValueOrDefault(test.Id);
        GUILayout.Label(test?.DisplayName ?? "No test selected", EditorStyles.boldLabel);
        if (result is not null && !string.IsNullOrWhiteSpace(result.Message))
            EditorGUILayout.HelpBox(result.Message,
                result.Status == TestStatus.Passed ? MessageType.Info : MessageType.Error);
        if (test is not null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.IsNullOrWhiteSpace(test.SourceFile) ? "Source unavailable" :
                $"{test.SourceFile}:{test.SourceLine}", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUI.enabled = !string.IsNullOrWhiteSpace(test.SourceFile);
            if (GUILayout.Button("Open", GUILayout.Width(70))) TestRunnerService.OpenSource(test);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
        if (!string.IsNullOrWhiteSpace(_coveragePath))
            GUILayout.Label("Coverage: " + _coveragePath, EditorStyles.miniLabel);
    }

    private IReadOnlyList<TestCase> VisibleTests() => _tests.Where(test =>
            (_mode == 0 || test.Mode == (TestMode)(_mode - 1)) &&
            (string.IsNullOrWhiteSpace(_search) ||
             test.DisplayName.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
             test.Category.Contains(_search, StringComparison.OrdinalIgnoreCase)))
        .ToArray();

    private void RefreshTests()
    {
        if (_operation is not null) return;
        try
        {
            _tests = TestRunnerService.Discover();
            _status = $"Discovered {_tests.Count} tests";
            if (_tests.All(test => test.Id != _selectedId)) _selectedId = _tests.FirstOrDefault()?.Id ?? string.Empty;
        }
        catch (Exception exception) { _status = exception.Message; Debug.LogException(exception); }
        Repaint();
    }

    private void Run(IReadOnlyList<TestCase> tests)
    {
        if (_operation is not null || tests.Count == 0) return;
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        foreach (var test in tests) _results.Remove(test.Id);
        _status = "Running";
        var token = _cancellation.Token;
        _operation = RunCoreAsync(tests, token);
    }

    private async Task RunCoreAsync(IReadOnlyList<TestCase> tests, CancellationToken cancellationToken)
    {
        var completed = new Dictionary<string, TestResult>(StringComparer.Ordinal);
        string status;
        try
        {
            var local = tests.Where(test => test.Mode != TestMode.Player).ToArray();
            if (local.Length > 0)
            {
                var result = await TestRunnerService.RunLocalAsync(local, _coverage,
                    report => EditorApplication.QueueMainThread(() =>
                    {
                        _results[report.TestId] = report;
                        Repaint();
                    }, "Test Runner result"), cancellationToken).ConfigureAwait(false);
                foreach (var item in result.Tests) completed[item.TestId] = item;
            }
            if (tests.Any(test => test.Mode == TestMode.Player))
            {
                var result = await PlayerTestSession.RunAsync(_coverage, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                foreach (var item in result.Tests) completed[item.TestId] = item;
            }
            status = cancellationToken.IsCancellationRequested ? "Canceled" : "Completed";
        }
        catch (OperationCanceledException) { status = "Canceled"; }
        catch (Exception exception)
        {
            status = "Failed: " + exception.Message;
            EditorApplication.QueueMainThread(() => Debug.LogException(exception), "Test Runner failure");
        }
        EditorApplication.QueueMainThread(() =>
        {
            foreach (var item in completed.Values) _results[item.TestId] = item;
            _status = status;
            Repaint();
        }, "Test Runner completion");
    }

    private void ExportCoverage()
    {
        var directory = Path.Combine(EditorApplication.projectPath, "Temp", "TestRunner", "Coverage");
        Directory.CreateDirectory(directory);
        var name = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var lcov = Path.Combine(directory, $"coverage-{name}.lcov");
        var json = Path.Combine(directory, $"coverage-{name}.json");
        var snapshot = CodeCoverage.Snapshot();
        CodeCoverage.WriteLcov(lcov, snapshot);
        CodeCoverage.WriteJson(json, snapshot);
        _coveragePath = lcov;
        _status = "Coverage exported";
    }
}
