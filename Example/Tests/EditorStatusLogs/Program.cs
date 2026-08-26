using System.Collections;
using System.Reflection;
using BEngine;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.EditorStatusLogs;

internal static class Program
{
    private const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly MethodInfo BeginFrame = RequireMethod(typeof(GUI), "BeginFrame",
        BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo EndFrame = RequireMethod(typeof(GUI), "EndFrame",
        BindingFlags.Static | BindingFlags.NonPublic);
    private static string? _openedFile;
    private static int _openedLine;
    private static int _openedColumn;

    [STAThread]
    private static int Main()
    {
        try
        {
            EditorAppearance.Apply(new EditorPreferencesDocument());
            EditorResource.RegisterResourceRoot(Path.GetFullPath(Path.Combine("src", "Core")));

            var statusType = typeof(EditorWindow).Assembly.GetType("BEngine.Editor.EditorStatusWindow",
                                 throwOnError: true)!;
            var status = Activator.CreateInstance(statusType, nonPublic: true) ??
                         throw new InvalidOperationException("Editor Status window could not be created.");
            var sourcePath = Path.GetFullPath(Path.Combine("Example", "Assets", "Editor", "ProjectMenus.cs"));
            var timestamp = new DateTimeOffset(2026, 8, 17, 19, 26, 43, 789, TimeSpan.FromHours(8));
            var entries = new[]
            {
                new LogEntry(timestamp, LogType.Info, "Editor opened project", "at Editor.Startup()"),
                new LogEntry(timestamp.AddSeconds(1), LogType.Warning,
                    "Importer warning first line\nImporter warning second line",
                    $"at Game.Editor.ProjectMenus.Import() in {sourcePath}:line 37"),
                new LogEntry(timestamp.AddSeconds(2), LogType.Error, "Compiler failed",
                    "at Compiler.Build()\nerror detail needle-in-stack")
            };

            VerifyCompleteEntryFormatting(entries[1]);
            VerifySessionLogStore();
            VerifyLogFileReaderAndStatusSource(status, statusType, sourcePath);
            VerifyStatusContract(statusType);
            PopulateEntries(status, statusType, entries);
            RequireMethod(statusType, "RebuildVisibleLogs", HiddenInstance).Invoke(status, null);
            VerifySearchAndTypeFilters(status, statusType, entries);
            VerifyListSelection(status, statusType, entries[1]);
            VerifyCompleteDetails(status, statusType, entries[1], sourcePath);
            VerifyClickableStackFrame(status, statusType, entries[1].StackTrace, sourcePath);

            Console.WriteLine(
                "EDITOR_STATUS_LOGS_OK|timestamp,type,message,stack,rows,search,filters,selection,details,file-line,highlight,open-asset");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifyCompleteEntryFormatting(LogEntry entry)
    {
        var detailed = entry.ToDetailedString();
        Require(detailed.Contains(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"),
                StringComparison.Ordinal), "A detailed editor log lost its full timestamp or UTC offset.");
        Require(detailed.Contains(nameof(LogType.Warning), StringComparison.Ordinal),
            "A detailed editor log lost its type.");
        Require(detailed.Contains("Importer warning first line", StringComparison.Ordinal) &&
                detailed.Contains("Importer warning second line", StringComparison.Ordinal),
            "A detailed editor log lost part of its multi-line message.");
        Require(detailed.Contains("ProjectMenus.Import()", StringComparison.Ordinal),
            "A detailed editor log lost its call stack.");
    }

    private static void VerifySessionLogStore()
    {
        var storeType = typeof(EditorWindow).Assembly.GetType("BEngine.Editor.EditorLogStore",
                            throwOnError: true)!;
        var initialize = RequireMethod(storeType, "Initialize", HiddenStatic);
        var clear = RequireMethod(storeType, "Clear", HiddenStatic);
        var snapshot = RequireMethod(storeType, "Snapshot", HiddenStatic);
        initialize.Invoke(null, null);
        clear.Invoke(null, null);

        BEngine.Debug.Log("STATUS_STORE_INFO");
        BEngine.Debug.LogWarning("STATUS_STORE_WARNING");
        BEngine.Debug.LogError("STATUS_STORE_ERROR");
        var logs = (LogEntry[])(snapshot.Invoke(null, null) ?? Array.Empty<LogEntry>());
        try
        {
            Require(logs.Length == 3, $"EditorLogStore retained {logs.Length} entries instead of 3.");
            Require(logs.Select(log => log.Type).SequenceEqual(
                    [LogType.Info, LogType.Warning, LogType.Error]),
                "EditorLogStore did not retain each log type in emission order.");
            Require(logs.Select(log => log.Message).SequenceEqual(
                    ["STATUS_STORE_INFO", "STATUS_STORE_WARNING", "STATUS_STORE_ERROR"]),
                "EditorLogStore did not retain each complete log message.");
            Require(logs.All(log => log.Timestamp != default),
                "EditorLogStore retained an entry without a timestamp.");
            Require(logs.All(log => log.StackTrace.Contains(nameof(VerifySessionLogStore),
                        StringComparison.Ordinal)),
                "EditorLogStore did not retain the original caller stack for every entry.");
        }
        finally { clear.Invoke(null, null); }
    }

    private static void VerifyLogFileReaderAndStatusSource(object status, Type statusType, string sourcePath)
    {
        var firstTimestamp = new DateTimeOffset(2026, 8, 17, 10, 12, 13, 456,
            TimeSpan.FromHours(8));
        var secondTimestamp = firstTimestamp.AddSeconds(1);
        var path = Path.Combine(Path.GetTempPath(), $"BEngine.EditorStatusLogs.{Guid.NewGuid():N}.log");
        try
        {
            File.WriteAllText(path,
                $"[{firstTimestamp:O}] [Warning] Importer retained its warning message{Environment.NewLine}" +
                $"   at Game.Editor.ProjectMenus.Import() in {sourcePath}:line 37{Environment.NewLine}" +
                $"[{secondTimestamp:O}] [Error] Compiler retained its error message{Environment.NewLine}" +
                $"at Compiler.Build() in {sourcePath}:line 41{Environment.NewLine}" +
                $"Legacy informational entry{Environment.NewLine}");

            var readerType = typeof(EditorWindow).Assembly.GetType("BEngine.Editor.EditorLogFileReader",
                                 throwOnError: true)!;
            var read = RequireMethodWithParameters(readerType, "Read", HiddenStatic, typeof(string));
            var parsed = (LogEntry[])(read.Invoke(null, [path]) ?? Array.Empty<LogEntry>());
            Require(parsed.Length == 3, $"EditorLogFileReader parsed {parsed.Length} entries instead of 3.");
            Require(parsed[0].Timestamp == firstTimestamp && parsed[0].Timestamp.Offset == firstTimestamp.Offset,
                "EditorLogFileReader did not preserve a structured timestamp and offset.");
            Require(parsed[0].Type == LogType.Warning &&
                    parsed[0].Message == "Importer retained its warning message",
                "EditorLogFileReader did not preserve the first entry type and message.");
            Require(parsed[0].StackTrace.Contains(sourcePath, StringComparison.OrdinalIgnoreCase) &&
                    parsed[0].StackTrace.Contains("line 37", StringComparison.Ordinal),
                "EditorLogFileReader did not attach the complete source frame to its entry.");
            Require(parsed[1].Timestamp == secondTimestamp && parsed[1].Type == LogType.Error &&
                    parsed[1].StackTrace.Contains("line 41", StringComparison.Ordinal),
                "EditorLogFileReader did not retain the second structured entry.");
            Require(parsed[2].Type == LogType.Info && parsed[2].Message == "Legacy informational entry",
                "EditorLogFileReader did not retain a legacy unstructured line as its own entry.");

            var paths = RequireField(statusType, "_logPaths").GetValue(status) as
                        IDictionary<string, string> ?? throw new InvalidOperationException(
                            "Editor Status log path map is unavailable.");
            paths.Clear();
            paths["Fixture.log"] = path;
            RequireField(statusType, "_sources").SetValue(status, new[] { "Fixture.log" });
            RequireField(statusType, "_sourceIndex").SetValue(status, 0);
            RequireMethodWithParameters(statusType, "RefreshLog", HiddenInstance).Invoke(status, null);
            var loaded = ((IEnumerable<LogEntry>)(RequireField(statusType, "_logs").GetValue(status) ??
                                                  Array.Empty<LogEntry>())).ToArray();
            Require(loaded.SequenceEqual(parsed),
                "Editor Status did not expose every parsed file record as an individual log entry.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void VerifyStatusContract(Type statusType)
    {
        Require(FindEntriesField(statusType) is not null,
            "Editor Status must retain individual LogEntry records instead of one concatenated text blob.");
        Require(statusType.GetField("_selected", HiddenInstance)?.FieldType == typeof(LogEntry?),
            "Editor Status has no selected LogEntry state for its details pane.");
        Require(RequireMethod(statusType, "RowText", HiddenStatic).ReturnType == typeof(string),
            "Editor Status has no timestamped row formatter.");
        _ = RequireMethod(statusType, "Show", HiddenInstance);
        _ = RequireMethod(statusType, "DrawLogList", HiddenInstance);
        _ = RequireMethod(statusType, "DrawDetails", HiddenInstance);
        _ = RequireMethod(statusType, "DrawStackTrace", HiddenInstance);

        var probe = new LogEntry(new DateTimeOffset(2026, 8, 17, 1, 2, 3, 4, TimeSpan.Zero),
            LogType.Warning, "first line\nsecond line", "stack");
        var rowText = (string)(RequireMethod(statusType, "RowText", HiddenStatic)
            .Invoke(null, [probe]) ?? string.Empty);
        var probeTime = probe.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
        Require(rowText.Contains(probeTime, StringComparison.Ordinal) &&
                rowText.Contains(nameof(LogType.Warning), StringComparison.Ordinal) &&
                rowText.Contains("first line second line", StringComparison.Ordinal),
            "Editor Status rows do not retain timestamp, type, and the complete flattened message.");
    }

    private static void VerifySearchAndTypeFilters(object status, Type statusType,
        IReadOnlyList<LogEntry> entries)
    {
        var show = RequireMethod(statusType, "Show", HiddenInstance);
        var search = RequireField(statusType, "_search");
        var info = RequireField(statusType, "_info");
        var warning = RequireField(statusType, "_warning");
        var error = RequireField(statusType, "_error");

        info.SetValue(status, true);
        warning.SetValue(status, true);
        error.SetValue(status, true);
        search.SetValue(status, "second line");
        Require(Show(show, status, entries[1]), "Editor Status search does not match a later message line.");
        search.SetValue(status, "needle-in-stack");
        Require(Show(show, status, entries[2]), "Editor Status search does not match call-stack text.");
        search.SetValue(status, nameof(LogType.Warning));
        Require(Show(show, status, entries[1]), "Editor Status search does not match a log type.");
        search.SetValue(status, entries[1].Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        Require(Show(show, status, entries[1]), "Editor Status search does not match a full timestamp.");
        search.SetValue(status, "missing editor log");
        Require(!Show(show, status, entries[0]), "Editor Status search retained an unrelated log.");

        search.SetValue(status, string.Empty);
        info.SetValue(status, false);
        warning.SetValue(status, true);
        error.SetValue(status, false);
        Require(!Show(show, status, entries[0]), "Editor Status info filter did not hide info logs.");
        Require(Show(show, status, entries[1]), "Editor Status warning filter hid warning logs.");
        Require(!Show(show, status, entries[2]), "Editor Status error filter did not hide error logs.");

        info.SetValue(status, true);
        warning.SetValue(status, true);
        error.SetValue(status, true);
    }

    private static void VerifyListSelection(object status, Type statusType, LogEntry expected)
    {
        var drawList = RequireMethod(statusType, "DrawLogList", HiddenInstance);
        var selected = RequireField(statusType, "_selected");
        selected.SetValue(status, null);
        var repaint = Render(status, drawList, new Event(EventType.Repaint), 1500, 420);
        var row = repaint.FirstOrDefault(command => command.Type == GpuCanvasCommandType.Text &&
                                                    command.Content.Contains("Importer warning first line",
                                                        StringComparison.Ordinal));
        Require(row.Type == GpuCanvasCommandType.Text,
            "Editor Status did not render every log as an individually selectable row.");
        var point = new Vector2((Fix64)(row.Rect.X + row.Rect.Width / 2),
            (Fix64)(row.Rect.Y + row.Rect.Height / 2));
        Render(status, drawList, new Event(EventType.MouseDown) { mousePosition = point, button = 0 }, 1500, 420);
        Render(status, drawList, new Event(EventType.MouseUp) { mousePosition = point, button = 0 }, 1500, 420);
        Require(selected.GetValue(status) is LogEntry value && value.Equals(expected),
            "Clicking an Editor Status log row did not select that complete LogEntry.");
    }

    private static void VerifyCompleteDetails(object status, Type statusType, LogEntry entry, string sourcePath)
    {
        var drawDetails = RequireMethod(statusType, "DrawDetails", HiddenInstance);
        var commands = Render(status, drawDetails, new Event(EventType.Repaint), 1800, 520, entry);
        var text = commands.Where(command => command.Type == GpuCanvasCommandType.Text)
            .Select(command => command.Content).ToArray();
        var localTimestamp = entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        Require(text.Any(value => value.Contains(localTimestamp, StringComparison.Ordinal)),
            "Editor Status details do not show the full local timestamp with milliseconds.");
        Require(text.Any(value => value.Contains(nameof(LogType.Warning), StringComparison.Ordinal)),
            "Editor Status details do not show the log type.");
        Require(text.Contains("Importer warning first line") && text.Contains("Importer warning second line"),
            "Editor Status details truncate a multi-line log message.");
        Require(text.Any(value => value.Contains("ProjectMenus.Import()", StringComparison.Ordinal)),
            "Editor Status details do not show the stack frame method.");
        Require(text.Any(value => value.Contains(Path.GetFileName(sourcePath), StringComparison.OrdinalIgnoreCase) &&
                                  value.Contains("37", StringComparison.Ordinal)),
            "Editor Status details do not show the stack frame file and line number.");
    }

    private static void VerifyClickableStackFrame(object status, Type statusType, string stackTrace,
        string sourcePath)
    {
        var drawStack = RequireMethod(statusType, "DrawStackTrace", HiddenInstance);
        var repaint = Render(status, drawStack, new Event(EventType.Repaint), 1800, 260, stackTrace);
        var linkColor = GpuCanvasColor.FromColor(EditorStyles.linkLabel.normal.textColor);
        var link = repaint.FirstOrDefault(command => command.Type == GpuCanvasCommandType.Text &&
                                                     command.Color == linkColor &&
                                                     command.Content.Contains(Path.GetFileName(sourcePath),
                                                         StringComparison.OrdinalIgnoreCase) &&
                                                     command.Content.Contains("37", StringComparison.Ordinal));
        Require(link.Type == GpuCanvasCommandType.Text,
            "Editor Status did not highlight a source file and line as a clickable link.");

        _openedFile = null;
        _openedLine = 0;
        _openedColumn = 0;
        Require(TypeCache.GetMethodsWithAttribute<OnOpenAssetAttribute>().Any(method =>
                method.Name == nameof(CaptureOpenAsset)),
            "The Editor Status OpenAsset test callback was not indexed.");
        var point = new Vector2((Fix64)(link.Rect.X + link.Rect.Width / 2),
            (Fix64)(link.Rect.Y + link.Rect.Height / 2));
        Render(status, drawStack, new Event(EventType.MouseDown) { mousePosition = point, button = 0 },
            1800, 260, stackTrace);
        Render(status, drawStack, new Event(EventType.MouseUp) { mousePosition = point, button = 0 },
            1800, 260, stackTrace);
        Require(_openedFile is not null && Path.GetFullPath(_openedFile)
                    .Equals(sourcePath, StringComparison.OrdinalIgnoreCase),
            $"Editor Status source link opened the wrong file: '{_openedFile}'.");
        Require(_openedLine == 37, $"Editor Status source link opened line {_openedLine} instead of 37.");
        Require(_openedColumn == -1,
            $"Editor Status source link supplied unexpected column {_openedColumn}.");
    }

    private static void PopulateEntries(object status, Type statusType, IReadOnlyCollection<LogEntry> entries)
    {
        var field = FindEntriesField(statusType) ??
                    throw new MissingFieldException(statusType.FullName, "LogEntry collection");
        if (!field.IsInitOnly && field.FieldType.IsAssignableFrom(typeof(List<LogEntry>)))
        {
            field.SetValue(status, entries.ToList());
            return;
        }

        if (field.GetValue(status) is ICollection<LogEntry> typed)
        {
            typed.Clear();
            foreach (var entry in entries) typed.Add(entry);
            return;
        }

        if (field.GetValue(status) is IList list)
        {
            list.Clear();
            foreach (var entry in entries) list.Add(entry);
            return;
        }

        throw new InvalidOperationException("Editor Status LogEntry collection cannot be populated for rendering.");
    }

    private static FieldInfo? FindEntriesField(Type statusType) =>
        statusType.GetField("_logs", HiddenInstance) ?? statusType.GetFields(HiddenInstance)
            .FirstOrDefault(field => field.FieldType != typeof(string) && IsLogEntryCollection(field.FieldType));

    private static bool IsLogEntryCollection(Type type)
    {
        if (type.IsArray) return type.GetElementType() == typeof(LogEntry);
        return type.GetInterfaces().Append(type).Any(candidate => candidate.IsGenericType &&
            candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>) &&
            candidate.GetGenericArguments()[0] == typeof(LogEntry));
    }

    private static bool Show(MethodInfo method, object status, LogEntry entry) =>
        (bool)(method.Invoke(status, [entry]) ?? false);

    private static List<GpuCanvasCommand> Render(object status, MethodInfo method, Event evt, int width,
        int height, params object?[] arguments)
    {
        var commands = new List<GpuCanvasCommand>();
        BeginFrame.Invoke(null, [evt, width, height, commands]);
        try { method.Invoke(status, arguments); }
        finally { EndFrame.Invoke(null, null); }
        return commands;
    }

    [OnOpenAsset(-10_000)]
    private static bool CaptureOpenAsset(string filePath, int lineNumber, int columnNumber)
    {
        _openedFile = filePath;
        _openedLine = lineNumber;
        _openedColumn = columnNumber;
        return true;
    }

    private static MethodInfo RequireMethod(Type type, string name, BindingFlags flags) =>
        type.GetMethod(name, flags) ?? throw new MissingMethodException(type.FullName, name);

    private static MethodInfo RequireMethodWithParameters(Type type, string name, BindingFlags flags,
        params Type[] parameterTypes) => type.GetMethod(name, flags, binder: null, parameterTypes,
        modifiers: null) ?? throw new MissingMethodException(type.FullName, name);

    private static FieldInfo RequireField(Type type, string name) =>
        type.GetField(name, HiddenInstance) ?? throw new MissingFieldException(type.FullName, name);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
