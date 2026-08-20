using System.Diagnostics;
using System.Text;

namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/EditorStatus.png")]
internal sealed class EditorStatusWindow : EditorWindow
{
    private const string SessionSource = "Current Session";
    private readonly Dictionary<string, string> _logPaths = new(StringComparer.OrdinalIgnoreCase);
    private string[] _sources = [];
    private int _sourceIndex;
    private string _search = string.Empty;
    private bool _info = true;
    private bool _warning = true;
    private bool _error = true;
    private List<LogEntry> _logs = [];
    private LogEntry[] _visibleLogs = [];
    private LogEntry? _selected;
    private Vector2 _listScroll;
    private Vector2 _detailsScroll;
    private Fix64 _detailsHeight;
    private Fix64 _detailsDragStartY;
    private Fix64 _detailsDragStartHeight;
    private Fix64 _detailsContentHeight = 1;
    private Fix64 _listContentWidth = 1;
    private string _summary = string.Empty;
    private string? _cachedStackTrace;
    private Fix64 _cachedStackFontSize = -1;
    private (string Line, ConsoleStackFrame? Frame)[] _stackLines = [];
    private Fix64 _stackContentWidth;
    private long _observedSessionVersion = -1;
    private string? _loadedPath;
    private long _loadedLength = -1;
    private DateTime _loadedWriteTime;
    private double _nextRefresh;
    private int _infoCount;
    private int _warningCount;
    private int _errorCount;
    private int _visibleCacheCount = -1;
    private CancellationTokenSource? _logReadCancellation;
    private int _logReadGeneration;

    [MenuItem("Window/Editor Status", false, 240)]
    public static void Open() => GetWindow<EditorStatusWindow>("Editor Status", true);

    protected override void OnEnable()
    {
        minSize = new Vector2(720, 420);
        EditorLogStore.Initialize();
        BuildLogPaths();
        RefreshLog();
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
        CancelLogRead();
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
        GUI.DrawRect(new Rect(separatorRect.x, separatorRect.y + 2, separatorRect.width, 2),
            EditorAppearance.palette.Border);
        using (GUILayout.Area(new Rect(0, contentY + listHeight + separatorHeight,
                   GUIUtility.currentViewWidth, detailsHeight)))
            DrawDetails(selected);
    }

    private void DrawToolbar()
    {
        GUI.DrawRect(new Rect(0, 0, GUIUtility.currentViewWidth,
            EditorStyles.toolbar.fixedHeight * 2 + 11), EditorAppearance.palette.Toolbar);
        GUILayout.BeginHorizontal(GUILayout.Height(EditorStyles.toolbar.fixedHeight));
        var selected = EditorGUILayout.Popup("Log", _sourceIndex, _sources,
            GUILayout.Width(Fix64.Min(340, Fix64.Max(190, GUIUtility.currentViewWidth / 3))));
        if (selected != _sourceIndex)
        {
            _sourceIndex = selected;
            ResetFileCache();
            RefreshLog();
        }
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Refresh, "Refresh log", GUILayout.Width(24)))
        {
            BuildLogPaths();
            RefreshLog();
        }
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.Clear, "Clear selected log", GUILayout.Width(24)))
            ClearSelectedLog();
        if (EditorToolbar.IconButton(EditorBuiltinIcons.Toolbar.OpenFolder, "Open log directory",
                GUILayout.Width(24))) OpenLogDirectory();
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
        _search = search;
        _info = info;
        _warning = warning;
        _error = error;
        RebuildVisibleLogs();
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

    private void BuildLogPaths()
    {
        var selectedSource = CurrentSource;
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Environment.CurrentDirectory;
        var logs = EditorInstanceContext.current?.logsPath ?? Path.Combine(projectRoot, "Logs");
        _logPaths.Clear();
        AddLogPath("EditorBootstrap.log", EditorDataPaths.editorBootstrapLogPath);
        AddLogPath("Editor.log", Path.Combine(logs, "Editor.log"));
        AddLogPath("ScriptCompilation.log", Path.Combine(logs, "ScriptCompilation.log"));
        AddLogPath("EditorScriptCompilation.log", Path.Combine(logs, "EditorScriptCompilation.log"));
        AddLogPath("LauncherBuild.log", EditorDataPaths.launcherBuildLogPath);
        if (Directory.Exists(logs))
        {
            foreach (var path in Directory.EnumerateFiles(logs, "*.log", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                AddLogPath(Path.GetFileName(path), path);
        }
        _sources = [SessionSource, .. _logPaths.Keys];
        var selectedIndex = Array.FindIndex(_sources,
            source => source.Equals(selectedSource, StringComparison.OrdinalIgnoreCase));
        _sourceIndex = selectedIndex < 0 ? 0 : selectedIndex;
    }

    private void AddLogPath(string name, string path)
    {
        if (_logPaths.TryGetValue(name, out var existing) &&
            !Path.GetFullPath(existing).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            name = $"{Path.GetFileNameWithoutExtension(name)} ({Path.GetFileName(Path.GetDirectoryName(path))}).log";
        _logPaths[name] = path;
    }

    private void RefreshLog() => RefreshLog(true);

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

        var info = new FileInfo(path);
        var length = info.Exists ? info.Length : 0;
        var writeTime = info.Exists ? info.LastWriteTimeUtc : default;
        if (!force && string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase) &&
            _loadedLength == length && _loadedWriteTime == writeTime) return;

        CancelLogRead();
        var cancellation = new CancellationTokenSource();
        _logReadCancellation = cancellation;
        var generation = ++_logReadGeneration;
        var read = scheduler.ScheduleAsync(
            $"Read editor log {Path.GetFileName(path)}",
            token =>
            {
                token.ThrowIfCancellationRequested();
                return File.Exists(path) ? EditorLogFileReader.Read(path) : [];
            },
            EditorTaskPriority.Background,
            cancellation.Token);
        _ = CompleteLogReadAsync(scheduler, read, generation, path, length, writeTime,
            info.Exists, cancellation.Token);
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
                $"Apply editor log {Path.GetFileName(path)}");
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
            _logs = [new LogEntry(DateTimeOffset.Now, LogType.Error,
                $"Could not read log file: {path}", failure.ToString())];
            _summary = path;
        }
        ApplyLogs();
    }

    private void RefreshLog(bool force)
    {
        if (CurrentSource == SessionSource)
        {
            _logs = EditorLogStore.SnapshotWithVersion(out _observedSessionVersion).ToList();
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
            var info = new FileInfo(path);
            var length = info.Exists ? info.Length : 0;
            var writeTime = info.Exists ? info.LastWriteTimeUtc : default;
            if (!force && string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase) &&
                _loadedLength == length && _loadedWriteTime == writeTime) return;
            _logs = info.Exists ? EditorLogFileReader.Read(path).ToList() : [];
            _loadedPath = path;
            _loadedLength = length;
            _loadedWriteTime = writeTime;
            _summary = info.Exists
                ? $"{path} | {_logs.Count:N0} entries | {FormatBytes(length)}"
                : $"Log file has not been created: {path}";
            ApplyLogs();
        }
        catch (Exception exception)
        {
            _logs = [new LogEntry(DateTimeOffset.Now, LogType.Error,
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

    private void RebuildVisibleLogs()
    {
        _visibleLogs = _logs.Where(Show).ToArray();
        _visibleCacheCount = _logs.Count;
        _listContentWidth = _visibleLogs.Aggregate(Fix64.Zero,
            (width, log) => Fix64.Max(width, MeasureLineWidth(RowText(log)) + 40));
    }

    private void ClearSelectedLog()
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
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Empty, new UTF8Encoding(false));
            ResetFileCache();
            RefreshLog();
        }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    private void OpenLogDirectory()
    {
        try
        {
            var directory = CurrentSource == SessionSource
                ? EditorInstanceContext.current?.logsPath ?? EditorDataPaths.logsPath
                : Path.GetDirectoryName(CurrentPath!);
            if (string.IsNullOrWhiteSpace(directory)) return;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
        }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    private void ResetFileCache()
    {
        CancelLogRead();
        _loadedPath = null;
        _loadedLength = -1;
        _loadedWriteTime = default;
        _listScroll = Vector2.zero;
        _detailsScroll = Vector2.zero;
        _selected = null;
    }

    private void CancelLogRead()
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
