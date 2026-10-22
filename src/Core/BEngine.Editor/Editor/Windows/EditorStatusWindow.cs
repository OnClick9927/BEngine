using BEngine.Platform;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/EditorStatus.png")]
public sealed class EditorStatusWindow :
    EditorWindow<EditorStatusWindowModel, EditorStatusWindowController>
{
    private const string SessionSource = "Current Session";
    private FileUtility fileSystem => controller.FileSystem;
    private PathUtility pathService => controller.PathService;
    private PlatformUtility platformService => controller.PlatformService;
    private ProcessHostUtility processHost => controller.ProcessHost;
    private Dictionary<string, string> _logPaths => model.LogPaths;
    private string[] _sources { get => model.Sources; set => model.Sources = value; }
    private int _sourceIndex { get => model.SourceIndex; set => model.SourceIndex = value; }
    private string _search { get => model.Search; set => model.Search = value; }
    private bool _info { get => model.ShowInfo; set => model.ShowInfo = value; }
    private bool _warning { get => model.ShowWarning; set => model.ShowWarning = value; }
    private bool _error { get => model.ShowError; set => model.ShowError = value; }
    private List<LogEntry> _logs { get => model.Logs; set => model.Logs = value; }
    private LogEntry[] _visibleLogs { get => model.VisibleLogs; set => model.VisibleLogs = value; }
    private LogEntry? _selected { get => model.Selected; set => model.Selected = value; }
    private Vector2 _listScroll { get => model.ListScroll; set => model.ListScroll = value; }
    private Vector2 _detailsScroll { get => model.DetailsScroll; set => model.DetailsScroll = value; }
    private Fix64 _detailsHeight { get => model.DetailsHeight; set => model.DetailsHeight = value; }
    private Fix64 _detailsDragStartY { get => model.DetailsDragStartY; set => model.DetailsDragStartY = value; }
    private Fix64 _detailsDragStartHeight { get => model.DetailsDragStartHeight; set => model.DetailsDragStartHeight = value; }
    private Fix64 _detailsContentHeight { get => model.DetailsContentHeight; set => model.DetailsContentHeight = value; }
    private Fix64 _listContentWidth { get => model.ListContentWidth; set => model.ListContentWidth = value; }
    private string _summary { get => model.Summary; set => model.Summary = value; }
    private string? _cachedStackTrace { get => model.CachedStackTrace; set => model.CachedStackTrace = value; }
    private Fix64 _cachedStackFontSize { get => model.CachedStackFontSize; set => model.CachedStackFontSize = value; }
    private (string Line, ConsoleStackFrame? Frame)[] _stackLines { get => model.StackLines; set => model.StackLines = value; }
    private Fix64 _stackContentWidth { get => model.StackContentWidth; set => model.StackContentWidth = value; }
    private long _observedSessionVersion { get => model.ObservedSessionVersion; set => model.ObservedSessionVersion = value; }
    private string? _loadedPath { get => model.LoadedPath; set => model.LoadedPath = value; }
    private long _loadedLength { get => model.LoadedLength; set => model.LoadedLength = value; }
    private DateTime _loadedWriteTime { get => model.LoadedWriteTime; set => model.LoadedWriteTime = value; }
    private double _nextRefresh { get => model.NextRefresh; set => model.NextRefresh = value; }
    private int _infoCount { get => model.InfoCount; set => model.InfoCount = value; }
    private int _warningCount { get => model.WarningCount; set => model.WarningCount = value; }
    private int _errorCount { get => model.ErrorCount; set => model.ErrorCount = value; }
    private int _visibleCacheCount { get => model.VisibleCacheCount; set => model.VisibleCacheCount = value; }
    private CancellationTokenSource? _logReadCancellation { get => model.LogReadCancellation; set => model.LogReadCancellation = value; }
    private int _logReadGeneration { get => model.LogReadGeneration; set => model.LogReadGeneration = value; }

    protected override EditorStatusWindowModel CreateWindowModel(EditorWindowMvcContext context) =>
        new(context.WindowId);

    protected override EditorStatusWindowController CreateWindowController(
        EditorWindowMvcContext context,
        EditorStatusWindowModel windowModel) => new(
        context,
        windowModel,
        EditorServiceAccess.GetRequiredService<FileUtility>(),
        EditorServiceAccess.GetRequiredService<PathUtility>(),
        EditorServiceAccess.GetRequiredService<PlatformUtility>(),
        EditorServiceAccess.GetRequiredService<ProcessHostUtility>());

    [MenuItem("Window/Editor Status", false, 240)]
    public static void Open() => GetWindow<EditorStatusWindow>("Editor Status", true);

    protected override void OnEnable()
    {
        minSize = new Vector2(720, 420);
        controller.Activate();
    }

    protected override void Update()
    {
        if (CurrentSource == SessionSource)
        {
            if (_observedSessionVersion != EditorLogStore.version) RefreshLog(false);
            return;
        }
        if (EditorApplication.timeSinceStartup < _nextRefresh) return;
        _nextRefresh = EditorApplication.timeSinceStartup + 0.75;
        BuildLogPaths();
        QueueLogRefresh(false);
    }

    protected override void OnDisable()
    {
        controller.Deactivate();
    }

    protected override void OnGUI()
    {
        DrawToolbar();
        GUILayout.Label(new GUIContent(_summary, CurrentPath ?? _summary), EditorStyles.miniLabel,
            GUILayout.ExpandWidth(true));

        var contentY = GUILayoutUtility.GetLastRect().yMax + 3;
        var availableHeight = Fix64.Max(1, GUIUtility.currentViewHeight - contentY - 4);
        var separatorHeight = _selected is null ? Fix64.Zero : (Fix64)6;
        var maximumDetailsHeight = Fix64.Max(0, availableHeight - 72 - separatorHeight);
        var minimumDetailsHeight = Fix64.Min(96, maximumDetailsHeight);
        if (_selected is not null && _detailsHeight <= 0)
            _detailsHeight = Fix64.Clamp(availableHeight * Fix64.FromDecimal(0.38m),
                minimumDetailsHeight, maximumDetailsHeight);
        var detailsHeight = _selected is null
            ? Fix64.Zero
            : Fix64.Clamp(_detailsHeight, minimumDetailsHeight, maximumDetailsHeight);
        var listHeight = Fix64.Max(1, availableHeight - detailsHeight - separatorHeight);
        var separatorRect = new Rect(0, contentY + listHeight, GUIUtility.currentViewWidth, separatorHeight);
        if (_selected is not null)
        {
            detailsHeight = HandleDetailsSplitter(separatorRect, availableHeight, detailsHeight);
            listHeight = Fix64.Max(1, availableHeight - detailsHeight - separatorHeight);
            separatorRect = new Rect(0, contentY + listHeight, GUIUtility.currentViewWidth, separatorHeight);
        }

        using (GUILayout.Area(new Rect(0, contentY, GUIUtility.currentViewWidth, listHeight)))
            DrawLogList();

        if (_selected is not { } selected || detailsHeight <= 0) return;
        GUI.Box(new Rect(separatorRect.x, separatorRect.y + 2, separatorRect.width, 2),
            GUIContent.none, EditorStyles.separator);
        using (GUILayout.Area(new Rect(0, contentY + listHeight + separatorHeight,
                   GUIUtility.currentViewWidth, detailsHeight)))
            DrawDetails(selected);
    }

    private void DrawToolbar()
    {
        GUI.Box(new Rect(0, 0, GUIUtility.currentViewWidth,
            EditorStyles.toolbar.fixedHeight * 2 + 11), GUIContent.none, EditorStyles.toolbar);
        GUILayout.BeginHorizontal(GUILayout.Height(EditorStyles.toolbar.fixedHeight));
        var selected = EditorGUILayout.Popup("Log", _sourceIndex, _sources,
            GUILayout.Width(Fix64.Min(340, Fix64.Max(190, GUIUtility.currentViewWidth / 3))));
        if (selected != _sourceIndex)
            controller.SelectSource(selected);
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Refresh, "Refresh log", GUILayout.Width(24)))
        {
            controller.Refresh();
        }
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Clear, "Clear selected log", GUILayout.Width(24)))
            controller.Clear();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.OpenFolder, "Open log directory",
                GUILayout.Width(24))) controller.OpenLogDirectory();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(GUILayout.Height(EditorStyles.toolbar.fixedHeight));
        var search = EditorToolbar.SearchField(_search, GUILayout.Width(Fix64.Max(60,
            GUIUtility.currentViewWidth - 174)));
        var info = EditorToolbar.Toggle(_info, new GUIContent(_infoCount.ToString(),
            EditorBuiltinIcons.Toolbar.Info, "Show info logs"), GUILayout.Width(52));
        var warning = EditorToolbar.Toggle(_warning, new GUIContent(_warningCount.ToString(),
            EditorBuiltinIcons.Toolbar.Warning, "Show warnings"), GUILayout.Width(52));
        var error = EditorToolbar.Toggle(_error, new GUIContent(_errorCount.ToString(),
            EditorBuiltinIcons.Toolbar.Error, "Show errors"), GUILayout.Width(52));
        GUILayout.EndHorizontal();
        if (search == _search && info == _info && warning == _warning && error == _error) return;
        controller.SetFilters(search, info, warning, error);
    }

    private void DrawLogList()
    {
        if (_visibleCacheCount != _logs.Count) RebuildVisibleLogs();
        var viewport = GUILayoutUtility.GetControlRect(60, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        var rowHeight = Fix64.Max(22, EditorStyles.treeViewRow.fixedHeight);
        var contentHeight = Fix64.Max(viewport.height, rowHeight * Math.Max(1, _visibleLogs.Length));
        var contentWidth = Fix64.Max(Fix64.Max(1, viewport.width - 11), _listContentWidth);
        _listScroll = GUI.BeginScrollView(viewport, _listScroll,
            new Rect(0, 0, contentWidth, contentHeight));
        try
        {
            if (_visibleLogs.Length == 0)
            {
                GUI.Label(new Rect(8, 5, Fix64.Max(1, contentWidth - 16), rowHeight),
                    _logs.Count == 0 ? "No log entries." : "No entries match the current filters.",
                    EditorStyles.miniLabel);
                return;
            }

            var first = Math.Clamp((int)(_listScroll.y / rowHeight) - 1, 0, _visibleLogs.Length - 1);
            var visibleCount = Math.Max(1, (int)(viewport.height / rowHeight) + 3);
            var last = Math.Min(_visibleLogs.Length, first + visibleCount);
            for (var index = first; index < last; index++)
            {
                var log = _visibleLogs[index];
                var rowRect = new Rect(0, rowHeight * index, contentWidth, rowHeight);
                var isSelected = _selected is { } current && current.Equals(log);
                if (GUI.Button(rowRect, new GUIContent(RowText(log), IconFor(log.Type),
                        log.ToDetailedString()), isSelected ? EditorStyles.treeViewRowSelected :
                        EditorStyles.treeViewRow)) _selected = log;

                var evt = Event.current;
                if (!rowRect.Contains(evt.mousePosition) || evt.type != EventType.ContextClick) continue;
                _selected = log;
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Copy Message"), false,
                    () => GUIUtility.systemCopyBuffer = log.Message);
                menu.AddItem(new GUIContent("Copy Full Log"), false,
                    () => GUIUtility.systemCopyBuffer = log.ToDetailedString());
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Clear"), false, ClearSelectedLog);
                menu.ShowAsContext();
                evt.Use();
            }
        }
        finally { GUI.EndScrollView(); }
    }

    private void DrawDetails(LogEntry log)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(new GUIContent(
                $"{log.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff zzz}  {log.Type}",
                IconFor(log.Type), "Log details"), EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("Copy", EditorStyles.toolbarButton, GUILayout.Width(58)))
            GUIUtility.systemCopyBuffer = log.ToDetailedString();
        GUILayout.EndHorizontal();
        GUILayout.Label($"Source: {CurrentSource}", EditorStyles.miniLabel);

        EnsureStackTraceCache(log.StackTrace ?? string.Empty);
        var contentWidth = Fix64.Max(_stackContentWidth, MeasureTextBlock(log.Message ?? "null")) + 12;
        var viewport = GUILayoutUtility.GetControlRect(60, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        _detailsScroll = GUI.BeginScrollView(viewport, _detailsScroll,
            new Rect(0, 0, Fix64.Max(viewport.width - 11, contentWidth),
                Fix64.Max(viewport.height, _detailsContentHeight)));
        GUILayout.BeginContainer(new Rect(0, 0, Fix64.Max(viewport.width - 11, contentWidth),
            Fix64.Max(viewport.height, _detailsContentHeight)));
        try
        {
            DrawLines(log.Message ?? "null");
            GUILayout.Space(5);
            GUILayout.Label("Call Stack", EditorStyles.boldLabel);
            if (string.IsNullOrWhiteSpace(log.StackTrace))
                GUILayout.Label("No call stack was recorded.", EditorStyles.miniLabel);
            else
                DrawStackTrace(log.StackTrace ?? string.Empty);
            _detailsContentHeight = Fix64.Max(1, GUILayout.CurrentContentHeight + 4);
        }
        finally
        {
            GUILayout.EndContainer();
            GUI.EndScrollView();
        }
    }

    private Fix64 HandleDetailsSplitter(Rect separatorRect, Fix64 availableHeight, Fix64 currentDetailsHeight)
    {
        var maximum = Fix64.Max(0, availableHeight - 72 - separatorRect.height);
        var minimum = Fix64.Min(96, maximum);
        currentDetailsHeight = Fix64.Clamp(currentDetailsHeight, minimum, maximum);
        var id = GUIUtility.GetControlID("EditorStatusDetailsSplitter".GetHashCode(StringComparison.Ordinal),
            FocusType.Passive, separatorRect);
        var evt = Event.current;
        EditorGUIUtility.AddCursorRect(GUIUtility.hotControl == id
                ? new Rect(0, 0, GUIUtility.currentViewWidth, GUIUtility.currentViewHeight)
                : separatorRect,
            MouseCursor.ResizeVertical);
        switch (evt.GetTypeForControl(id))
        {
            case EventType.MouseDown when evt.button == 0 && separatorRect.Contains(evt.mousePosition):
                GUIUtility.hotControl = id;
                _detailsDragStartY = evt.mousePosition.y;
                _detailsDragStartHeight = currentDetailsHeight;
                evt.Use();
                break;
            case EventType.MouseDrag when GUIUtility.hotControl == id:
                currentDetailsHeight = Fix64.Clamp(
                    _detailsDragStartHeight - (evt.mousePosition.y - _detailsDragStartY), minimum, maximum);
                _detailsHeight = currentDetailsHeight;
                evt.Use();
                Repaint();
                break;
            case EventType.MouseUp when GUIUtility.hotControl == id:
                GUIUtility.hotControl = 0;
                evt.Use();
                break;
        }
        _detailsHeight = currentDetailsHeight;
        return currentDetailsHeight;
    }

    private void DrawStackTrace(string stackTrace)
    {
        EnsureStackTraceCache(stackTrace);
        foreach (var (line, frame) in _stackLines) DrawDetailLine(line, frame);
    }

    private void DrawLines(string text)
    {
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var frame = ConsoleStackTraceParser.TryParse(line, out var parsed)
                ? (ConsoleStackFrame?)parsed
                : null;
            DrawDetailLine(line, frame);
        }
    }

    private static void DrawDetailLine(string line, ConsoleStackFrame? frame)
    {
        var rowHeight = Fix64.Max(22,
            GUITextMetrics.MeasureLineHeight(EditorStyles.label.fontSize, GUIUtility.fontFamily) + 1);
        if (frame is not { } source)
        {
            GUILayout.Label(string.IsNullOrEmpty(line) ? " " : line, GUILayout.Height(rowHeight),
                GUILayout.Width(Fix64.Max(1, MeasureLineWidth(line) + 8)));
            return;
        }
        if (!string.IsNullOrWhiteSpace(source.Prefix))
            GUILayout.Label(source.Prefix, GUILayout.Height(rowHeight),
                GUILayout.Width(Fix64.Max(1, MeasureLineWidth(source.Prefix) + 8)));
        var content = new GUIContent(source.SourceLocation,
            $"Open {source.FilePath} at line {source.LineNumber}");
        if (GUILayout.Button(content, EditorStyles.linkLabel,
                GUILayout.Width(Fix64.Max(40, MeasureLineWidth(source.SourceLocation) + 8)),
                GUILayout.Height(rowHeight)))
            AssetDatabase.OpenAsset(source.FilePath, source.LineNumber,
                source.ColumnNumber > 0 ? source.ColumnNumber : -1);
        EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
    }

    private void EnsureStackTraceCache(string stackTrace)
    {
        var fontSize = EditorStyles.label.fontSize;
        if (string.Equals(_cachedStackTrace, stackTrace, StringComparison.Ordinal) &&
            _cachedStackFontSize == fontSize) return;
        _cachedStackTrace = stackTrace;
        _cachedStackFontSize = fontSize;
        _stackLines = stackTrace.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(line => ConsoleStackTraceParser.TryParse(line, out var frame)
                ? (line, (ConsoleStackFrame?)frame)
                : (line, (ConsoleStackFrame?)null))
            .ToArray();
        _stackContentWidth = _stackLines.Aggregate(Fix64.Zero, (width, item) =>
        {
            var lineWidth = item.Frame is { } source
                ? Fix64.Max(MeasureLineWidth(source.Prefix), MeasureLineWidth(source.SourceLocation))
                : MeasureLineWidth(item.Line);
            return Fix64.Max(width, lineWidth + 8);
        });
    }

    internal void BuildLogPaths()
    {
        var selectedSource = CurrentSource;
        var projectRoot = pathService.GetDirectoryName(Application.dataPath) ??
                          platformService.CurrentDirectory;
        var logs = EditorInstanceContext.current?.logsPath ?? pathService.Combine(projectRoot, "Logs");
        _logPaths.Clear();
        AddLogPath("EditorBootstrap.log",
            EditorDataPaths.GetProjectLogPath(projectRoot, "EditorBootstrap.log"));
        AddLogPath("Editor.log", pathService.Combine(logs, "Editor.log"));
        AddLogPath("ScriptCompilation.log", pathService.Combine(logs, "ScriptCompilation.log"));
        AddLogPath("EditorScriptCompilation.log", pathService.Combine(logs, "EditorScriptCompilation.log"));
        AddLogPath("LauncherBuild.log",
            EditorDataPaths.GetProjectLogPath(projectRoot, "LauncherBuild.log"));
        if (fileSystem.DirectoryExists(logs))
        {
            foreach (var path in fileSystem.EnumerateFiles(logs, "*.log", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                AddLogPath(pathService.GetFileName(path), path);
        }
        _sources = [SessionSource, .. _logPaths.Keys];
        var selectedIndex = Array.FindIndex(_sources,
            source => source.Equals(selectedSource, StringComparison.OrdinalIgnoreCase));
        _sourceIndex = selectedIndex < 0 ? 0 : selectedIndex;
    }

    private void AddLogPath(string name, string path)
    {
        if (_logPaths.TryGetValue(name, out var existing) &&
            !pathService.GetFullPath(existing).Equals(
                pathService.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            name = $"{pathService.GetFileNameWithoutExtension(name)} " +
                   $"({pathService.GetFileName(pathService.GetDirectoryName(path) ?? string.Empty)}).log";
        _logPaths[name] = path;
    }

    internal void RefreshLog() => RefreshLog(true);

    private void QueueLogRefresh(bool force)
    {
        if (CurrentSource == SessionSource || EditorApplication.TaskScheduler is not { } scheduler)
        {
            RefreshLog(force);
            return;
        }

        var path = CurrentPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            RefreshLog(force);
            return;
        }

        var exists = fileSystem.FileExists(path);
        var length = exists ? fileSystem.GetFileLength(path) : 0;
        var writeTime = exists ? fileSystem.GetLastWriteTimeUtc(path) : default;
        if (!force && string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase) &&
            _loadedLength == length && _loadedWriteTime == writeTime) return;

        CancelLogRead();
        var cancellation = new CancellationTokenSource();
        _logReadCancellation = cancellation;
        var generation = ++_logReadGeneration;
        var read = scheduler.ScheduleAsync(
            $"Read editor log {pathService.GetFileName(path)}",
            token =>
            {
                token.ThrowIfCancellationRequested();
                return fileSystem.FileExists(path)
                    ? EditorLogFileReader.Read(path, fileSystem)
                    : [];
            },
            EditorTaskPriority.Background,
            cancellation.Token);
        _ = CompleteLogReadAsync(scheduler, read, generation, path, length, writeTime,
            exists, cancellation.Token);
    }

    private async Task CompleteLogReadAsync(
        IEditorTaskScheduler scheduler,
        Task<LogEntry[]> read,
        int generation,
        string path,
        long length,
        DateTime writeTime,
        bool existed,
        CancellationToken cancellationToken)
    {
        LogEntry[] logs;
        Exception? failure = null;
        try { logs = await read.ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        catch (Exception exception)
        {
            logs = [];
            failure = exception;
        }

        try
        {
            scheduler.Post(() => ApplyLogRead(generation, path, length, writeTime, existed, logs, failure),
                $"Apply editor log {pathService.GetFileName(path)}");
        }
        catch (ObjectDisposedException) { }
    }

    private void ApplyLogRead(
        int generation,
        string path,
        long length,
        DateTime writeTime,
        bool existed,
        LogEntry[] logs,
        Exception? failure)
    {
        if (generation != _logReadGeneration ||
            !string.Equals(CurrentPath, path, StringComparison.OrdinalIgnoreCase)) return;
        _logReadCancellation?.Dispose();
        _logReadCancellation = null;
        _loadedPath = path;
        _loadedLength = length;
        _loadedWriteTime = writeTime;
        if (failure is null)
        {
            _logs = logs.ToList();
            _summary = existed
                ? $"{path} | {_logs.Count:N0} entries | {FormatBytes(length)}"
                : $"Log file has not been created: {path}";
        }
        else
        {
            _logs = [new LogEntry(EditorHostServices.Platform.UtcNow.ToLocalTime(), LogType.Error,
                $"Could not read log file: {path}", failure.ToString())];
            _summary = path;
        }
        ApplyLogs();
    }

    internal void RefreshLog(bool force)
    {
        if (CurrentSource == SessionSource)
        {
            _logs = EditorLogStore.SnapshotWithVersion(out var observedSessionVersion).ToList();
            _observedSessionVersion = observedSessionVersion;
            _summary = $"Current editor session | {_logs.Count:N0} entries";
            ApplyLogs();
            return;
        }

        var path = CurrentPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            _logs = [];
            _summary = "No log source is selected.";
            ApplyLogs();
            return;
        }
        try
        {
            var exists = fileSystem.FileExists(path);
            var length = exists ? fileSystem.GetFileLength(path) : 0;
            var writeTime = exists ? fileSystem.GetLastWriteTimeUtc(path) : default;
            if (!force && string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase) &&
                _loadedLength == length && _loadedWriteTime == writeTime) return;
            _logs = exists ? EditorLogFileReader.Read(path, fileSystem).ToList() : [];
            _loadedPath = path;
            _loadedLength = length;
            _loadedWriteTime = writeTime;
            _summary = exists
                ? $"{path} | {_logs.Count:N0} entries | {FormatBytes(length)}"
                : $"Log file has not been created: {path}";
            ApplyLogs();
        }
        catch (Exception exception)
        {
            _logs = [new LogEntry(EditorHostServices.Platform.UtcNow.ToLocalTime(), LogType.Error,
                $"Could not read log file: {path}", exception.ToString())];
            _summary = path;
            ApplyLogs();
        }
    }

    private void ApplyLogs()
    {
        if (_selected is { } selected && !_logs.Contains(selected)) _selected = null;
        _infoCount = _logs.Count(log => log.Type is not LogType.Warning and not LogType.Error);
        _warningCount = _logs.Count(log => log.Type == LogType.Warning);
        _errorCount = _logs.Count(log => log.Type == LogType.Error);
        RebuildVisibleLogs();
        Repaint();
    }

    internal void RebuildVisibleLogs()
    {
        _visibleLogs = _logs.Where(Show).ToArray();
        _visibleCacheCount = _logs.Count;
        _listContentWidth = _visibleLogs.Aggregate(Fix64.Zero,
            (width, log) => Fix64.Max(width, MeasureLineWidth(RowText(log)) + 40));
    }

    internal void ClearSelectedLog()
    {
        try
        {
            if (CurrentSource == SessionSource)
            {
                EditorLogStore.Clear();
                RefreshLog();
                return;
            }
            var path = CurrentPath;
            if (string.IsNullOrWhiteSpace(path)) return;
            var directory = pathService.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) fileSystem.CreateDirectory(directory);
            fileSystem.WriteAllText(path, string.Empty);
            ResetFileCache();
            RefreshLog();
        }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    internal void OpenLogDirectory()
    {
        try
        {
            var directory = CurrentSource == SessionSource
                ? EditorInstanceContext.current?.logsPath ?? pathService.Combine(
                    pathService.GetDirectoryName(Application.dataPath) ?? platformService.CurrentDirectory, "Logs")
                : pathService.GetDirectoryName(CurrentPath!);
            if (string.IsNullOrWhiteSpace(directory)) return;
            fileSystem.CreateDirectory(directory);
            var request = new ProcessHostRequest("explorer.exe")
            {
                UseShellExecute = true,
                CreateNoWindow = false
            };
            request.Arguments.Add(directory);
            processHost.StartDetached(request);
        }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    internal void ResetFileCache()
    {
        CancelLogRead();
        _loadedPath = null;
        _loadedLength = -1;
        _loadedWriteTime = default;
        _listScroll = Vector2.zero;
        _detailsScroll = Vector2.zero;
        _selected = null;
    }

    internal void CancelLogRead()
    {
        _logReadGeneration++;
        var cancellation = _logReadCancellation;
        _logReadCancellation = null;
        if (cancellation is null) return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private bool Show(LogEntry item) =>
        (string.IsNullOrWhiteSpace(_search) ||
         item.Message.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
         item.StackTrace.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
         item.Type.ToString().Contains(_search, StringComparison.OrdinalIgnoreCase) ||
         item.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff").Contains(_search,
             StringComparison.OrdinalIgnoreCase)) &&
        item.Type switch { LogType.Warning => _warning, LogType.Error => _error, _ => _info };

    private static string RowText(LogEntry log) =>
        $"[{log.Timestamp.ToLocalTime():HH:mm:ss.fff}] [{log.Type}] " +
        (log.Message ?? "null").ReplaceLineEndings(" ");

    private static string IconFor(LogType type) => type switch
    {
        LogType.Warning => EditorBuiltinIcons.Toolbar.Warning,
        LogType.Error => EditorBuiltinIcons.Toolbar.Error,
        _ => EditorBuiltinIcons.Toolbar.Info
    };

    private static Fix64 MeasureLineWidth(string text) =>
        GUITextMetrics.MeasureWidth(text ?? string.Empty, EditorStyles.label.fontSize, GUIUtility.fontFamily);

    private static Fix64 MeasureTextBlock(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n').Aggregate(Fix64.Zero, (width, line) => Fix64.Max(width, MeasureLineWidth(line)));

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{bytes / 1024d:0.0} KB",
        _ => $"{bytes} B"
    };

    private string CurrentSource => _sources.Length == 0
        ? SessionSource
        : _sources[Math.Clamp(_sourceIndex, 0, _sources.Length - 1)];

    private string? CurrentPath => CurrentSource == SessionSource ||
                                   !_logPaths.TryGetValue(CurrentSource, out var path)
        ? null
        : path;
}

public sealed class EditorStatusWindowModel(string windowId) : EditorWindowModel(windowId)
{
    internal Dictionary<string, string> LogPaths { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public string[] Sources { get; internal set; } = [];
    public int SourceIndex { get; internal set; }
    public string Search { get; internal set; } = string.Empty;
    public bool ShowInfo { get; internal set; } = true;
    public bool ShowWarning { get; internal set; } = true;
    public bool ShowError { get; internal set; } = true;
    internal List<LogEntry> Logs { get; set; } = [];
    internal LogEntry[] VisibleLogs { get; set; } = [];
    public IReadOnlyList<LogEntry> LogEntries => Logs;
    public IReadOnlyList<LogEntry> VisibleLogEntries => VisibleLogs;
    public LogEntry? Selected { get; internal set; }
    internal Vector2 ListScroll { get; set; }
    internal Vector2 DetailsScroll { get; set; }
    internal Fix64 DetailsHeight { get; set; }
    internal Fix64 DetailsDragStartY { get; set; }
    internal Fix64 DetailsDragStartHeight { get; set; }
    internal Fix64 DetailsContentHeight { get; set; } = 1;
    internal Fix64 ListContentWidth { get; set; } = 1;
    public string Summary { get; internal set; } = string.Empty;
    internal string? CachedStackTrace { get; set; }
    internal Fix64 CachedStackFontSize { get; set; } = -1;
    internal (string Line, ConsoleStackFrame? Frame)[] StackLines { get; set; } = [];
    internal Fix64 StackContentWidth { get; set; }
    internal long ObservedSessionVersion { get; set; } = -1;
    internal string? LoadedPath { get; set; }
    internal long LoadedLength { get; set; } = -1;
    internal DateTime LoadedWriteTime { get; set; }
    internal double NextRefresh { get; set; }
    public int InfoCount { get; internal set; }
    public int WarningCount { get; internal set; }
    public int ErrorCount { get; internal set; }
    internal int VisibleCacheCount { get; set; } = -1;
    internal CancellationTokenSource? LogReadCancellation { get; set; }
    internal int LogReadGeneration { get; set; }
}

public sealed class EditorStatusWindowController :
    EditorWindowController<EditorStatusWindow, EditorStatusWindowModel>
{
    public EditorStatusWindowController(
        EditorWindowMvcContext context,
        EditorStatusWindowModel model,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        ProcessHostUtility processHost) : base(context, model)
    {
        FileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        PathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        PlatformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        ProcessHost = processHost ?? throw new ArgumentNullException(nameof(processHost));
    }

    internal FileUtility FileSystem { get; }
    internal PathUtility PathService { get; }
    internal PlatformUtility PlatformService { get; }
    internal ProcessHostUtility ProcessHost { get; }

    public void Activate()
    {
        EditorLogStore.Initialize();
        View.BuildLogPaths();
        View.RefreshLog();
    }

    public void Deactivate() => View.CancelLogRead();

    public void Refresh()
    {
        View.BuildLogPaths();
        View.RefreshLog();
    }

    public void SelectSource(int sourceIndex)
    {
        Model.SourceIndex = Math.Clamp(sourceIndex, 0, Math.Max(0, Model.Sources.Length - 1));
        View.ResetFileCache();
        View.RefreshLog();
    }

    public void SetFilters(string search, bool showInfo, bool showWarning, bool showError)
    {
        Model.Search = search ?? string.Empty;
        Model.ShowInfo = showInfo;
        Model.ShowWarning = showWarning;
        Model.ShowError = showError;
        View.RebuildVisibleLogs();
        Repaint();
    }

    public void Clear() => View.ClearSelectedLog();

    public void OpenLogDirectory() => View.OpenLogDirectory();
}
