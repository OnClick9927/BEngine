using System.Reflection;
using BEngine;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.ConsoleLogDetails;

internal static class Program
{
    private static LogEntry? _lastEntry;
    private static string? _openedFile;
    private static int _openedLine;
    private static int _openedColumn;
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod("BeginFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod("EndFrame",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private static int Main()
    {
        EditorAppearance.Apply(new EditorPreferencesDocument());
        BEngine.Debug.MessageLogged += Capture;
        try
        {
            var before = DateTimeOffset.Now;
            EmitLog();
            var after = DateTimeOffset.Now;
            var entry = RequireEntry();
            Require(entry.Type == LogType.Info, "Log type was not retained.");
            Require(entry.Message == "CONSOLE_DETAIL_TEST", "Log message was not retained.");
            Require(entry.Timestamp >= before && entry.Timestamp <= after, "Log timestamp is invalid.");
            Require(entry.StackTrace.Contains(nameof(EmitLog), StringComparison.Ordinal),
                "Debug.Log did not capture its caller stack.");
            Require(entry.ToDetailedString().Contains(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                StringComparison.Ordinal), "Detailed log does not include its timestamp.");

            EmitException();
            var exceptionEntry = RequireEntry();
            Require(exceptionEntry.Type == LogType.Error, "LogException must create an error log.");
            Require(exceptionEntry.StackTrace.Contains(nameof(ThrowTestException), StringComparison.Ordinal),
                "LogException did not retain the exception stack.");

            EmitWarning();
            var warningEntry = RequireEntry();
            Require(warningEntry.Type == LogType.Warning,
                "Debug.LogWarning must create a warning log for the Console.");
            Require(warningEntry.Message == "CONSOLE_WARNING_TEST",
                "The Console warning message was not retained.");

            var consoleType = typeof(EditorWindow).Assembly.GetType(
                "BEngine.Editor.GpuEditorApplication+ImGuiConsoleWindow", throwOnError: true)!;
            Require(consoleType.GetField("_selected", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
                "Console has no selected log state.");
            Require(consoleType.GetMethod("DrawDetails", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
                "Console has no log details view.");
            Require(consoleType.GetMethod("RowText", BindingFlags.Static | BindingFlags.NonPublic) is not null,
                "Console has no timestamped row formatter.");

            VerifyDetailsSplitter(consoleType);
            VerifyStackTraceParser();
            VerifyStackTraceLink(consoleType);
            VerifySelectableLogText(consoleType);

            Console.WriteLine(
                "CONSOLE_LOG_DETAILS_OK|timestamp,stack,selection,details,exception,warning,splitter,limits,file-line,highlight,open-asset,select-copy");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            BEngine.Debug.MessageLogged -= Capture;
        }
    }

    private static void EmitLog() => BEngine.Debug.Log("CONSOLE_DETAIL_TEST");

    private static void EmitWarning() => BEngine.Debug.LogWarning("CONSOLE_WARNING_TEST");

    private static void EmitException()
    {
        try { ThrowTestException(); }
        catch (InvalidOperationException exception) { BEngine.Debug.LogException(exception); }
    }

    private static void ThrowTestException() => throw new InvalidOperationException("CONSOLE_EXCEPTION_TEST");
    private static void Capture(LogEntry entry) => _lastEntry = entry;
    private static LogEntry RequireEntry() => _lastEntry ?? throw new InvalidOperationException("No log received.");

    private static void VerifyDetailsSplitter(Type consoleType)
    {
        var console = Activator.CreateInstance(consoleType, nonPublic: true) ??
                      throw new InvalidOperationException("Console window could not be created.");
        var heightField = consoleType.GetField("_detailsHeight", BindingFlags.Instance | BindingFlags.NonPublic) ??
                          throw new MissingFieldException(consoleType.FullName, "_detailsHeight");
        var splitter = consoleType.GetMethod("HandleDetailsSplitter",
            BindingFlags.Instance | BindingFlags.NonPublic) ??
                       throw new MissingMethodException(consoleType.FullName, "HandleDetailsSplitter");
        Require(splitter.GetParameters().Length == 3,
            "Console detail splitter must receive its hit rectangle, available height, and current height.");

        var availableHeight = (Fix64)400;
        var initialHeight = (Fix64)150;
        var separator = new Rect(0, 240, 800, 6);
        heightField.SetValue(console, initialHeight);
        var enlarged = DragSplitter(console, splitter, heightField, separator, availableHeight,
            initialHeight, new Vector2(120, 243), new Vector2(120, 183));
        Require(enlarged > initialHeight,
            "Dragging the Console detail splitter upward did not increase detail height.");

        heightField.SetValue(console, initialHeight);
        var minimum = DragSplitter(console, splitter, heightField, separator, availableHeight,
            initialHeight, new Vector2(120, 243), new Vector2(120, 1200));
        Require(minimum >= (Fix64)96,
            $"Console details can shrink below the readable minimum: {minimum}.");

        heightField.SetValue(console, initialHeight);
        var maximum = DragSplitter(console, splitter, heightField, separator, availableHeight,
            initialHeight, new Vector2(120, 243), new Vector2(120, -1200));
        Require(maximum <= availableHeight - 72,
            $"Console details can hide the minimum log-list area: {maximum}.");
        Require(GUIUtility.hotControl == 0, "Console detail splitter did not release its hot control.");
    }

    private static Fix64 DragSplitter(object console, MethodInfo splitter, FieldInfo heightField, Rect separator,
        Fix64 availableHeight, Fix64 currentHeight, Vector2 start, Vector2 finish)
    {
        currentHeight = DispatchSplitter(console, splitter, heightField, separator, availableHeight,
            currentHeight, new Event(EventType.MouseDown) { mousePosition = start, button = 0 });
        currentHeight = DispatchSplitter(console, splitter, heightField, separator, availableHeight,
            currentHeight, new Event(EventType.MouseDrag)
            {
                mousePosition = finish,
                delta = finish - start,
                button = 0
            });
        return DispatchSplitter(console, splitter, heightField, separator, availableHeight,
            currentHeight, new Event(EventType.MouseUp) { mousePosition = finish, button = 0 });
    }

    private static Fix64 DispatchSplitter(object console, MethodInfo splitter, FieldInfo heightField,
        Rect separator, Fix64 availableHeight, Fix64 currentHeight, Event evt)
    {
        BeginFrame.Invoke(null, [evt, 900, 500, new List<GpuCanvasCommand>()]);
        try
        {
            var result = splitter.Invoke(console, [separator, availableHeight, currentHeight]);
            if (result is Fix64 returnedHeight) return returnedHeight;
            return (Fix64)(heightField.GetValue(console) ?? currentHeight);
        }
        finally { EndFrame.Invoke(null, null); }
    }

    private static void VerifyStackTraceParser()
    {
        var editorAssembly = typeof(EditorWindow).Assembly;
        var parserType = editorAssembly.GetType("BEngine.Editor.ConsoleStackTraceParser", throwOnError: true)!;
        var parser = parserType.GetMethod("TryParse", BindingFlags.Static | BindingFlags.Public |
                                                     BindingFlags.NonPublic) ??
                     throw new MissingMethodException(parserType.FullName, "TryParse");
        var sourcePath = Path.GetFullPath(Path.Combine("Example", "Assets", "Editor", "ProjectMenus.cs"));
        var sourceLine = $"   at Game.Editor.ProjectMenus.LogSelectedObject() in {sourcePath}:line 11";
        object?[] arguments = [sourceLine, null];
        Require((bool)(parser.Invoke(null, arguments) ?? false),
            "Console stack parser rejected a standard .NET file/line frame.");
        var frame = arguments[1] ?? throw new InvalidOperationException("Parsed stack frame was not returned.");
        var filePath = ReadMember<string>(frame, "FilePath", "File", "Path");
        var lineNumber = ReadMember<int>(frame, "LineNumber", "Line");
        Require(Path.GetFullPath(filePath).Equals(sourcePath, StringComparison.OrdinalIgnoreCase),
            $"Stack frame file path was parsed incorrectly: '{filePath}'.");
        Require(lineNumber == 11, $"Stack frame line number was parsed incorrectly: {lineNumber}.");

        arguments = ["   at Game.Editor.ProjectMenus.LogSelectedObject()", null];
        Require(!(bool)(parser.Invoke(null, arguments) ?? false),
            "Console stack parser treated a frame without source information as a clickable link.");
    }

    private static void VerifyStackTraceLink(Type consoleType)
    {
        var drawStackTrace = consoleType.GetMethod("DrawStackTrace", BindingFlags.Instance |
                                                                     BindingFlags.NonPublic) ??
                             throw new MissingMethodException(consoleType.FullName, "DrawStackTrace");
        var console = Activator.CreateInstance(consoleType, nonPublic: true) ??
                      throw new InvalidOperationException("Console window could not be created.");
        var sourcePath = Path.GetFullPath(Path.Combine("Example", "Assets", "Editor", "ProjectMenus.cs"));
        var stackTrace = $"at Game.Editor.ProjectMenus.LogSelectedObject() in {sourcePath}:line 11";
        var commands = RenderStackTrace(console, drawStackTrace, stackTrace, new Event(EventType.Repaint));

        var linkStyle = typeof(EditorStyles).GetProperty("linkLabel", BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as GUIStyle ??
                        throw new MissingMemberException(typeof(EditorStyles).FullName, "linkLabel");
        var linkColor = GpuCanvasColor.FromColor(linkStyle.normal.textColor);
        Require(linkColor != GpuCanvasColor.FromColor(EditorStyles.label.normal.textColor),
            "Console source links are not visually distinguished from normal detail text.");
        var link = commands.FirstOrDefault(command => command.Type == GpuCanvasCommandType.Text &&
                                                       command.Color == linkColor &&
                                                       command.Content.Contains("ProjectMenus.cs",
                                                           StringComparison.OrdinalIgnoreCase));
        Require(link.Type == GpuCanvasCommandType.Text,
            "A parsed Console stack frame did not render a highlighted source link.");

        VerifyOpenAssetApi(sourcePath);
        _openedFile = null;
        _openedLine = 0;
        _openedColumn = 0;
        Require(TypeCache.GetMethodsWithAttribute<OnOpenAssetAttribute>().Any(method =>
                method.Name == nameof(CaptureOpenAsset)),
            "The test OpenAsset callback was not indexed by the editor reflection cache.");
        var position = new Vector2((Fix64)(link.Rect.X + link.Rect.Width / 2),
            (Fix64)(link.Rect.Y + link.Rect.Height / 2));
        RenderStackTrace(console, drawStackTrace, stackTrace,
            new Event(EventType.MouseDown) { mousePosition = position, button = 0 });
        RenderStackTrace(console, drawStackTrace, stackTrace,
            new Event(EventType.MouseUp) { mousePosition = position, button = 0 });
        Require(_openedFile is not null && Path.GetFullPath(_openedFile)
                    .Equals(sourcePath, StringComparison.OrdinalIgnoreCase),
            $"Clicking a Console source link opened the wrong file: '{_openedFile}'.");
        Require(_openedLine == 11,
            $"Clicking a Console source link opened the wrong line: {_openedLine}.");
        Require(_openedColumn == -1,
            $"Clicking a Console source link supplied an unexpected column: {_openedColumn}.");
    }

    private static void VerifyOpenAssetApi(string sourcePath)
    {
        var openAsset = typeof(AssetDatabase).GetMethod("OpenAsset", BindingFlags.Static | BindingFlags.Public,
            binder: null, [typeof(string), typeof(int), typeof(int)], modifiers: null) ??
                        throw new MissingMethodException(typeof(AssetDatabase).FullName,
                            "OpenAsset(string, int, int)");
        _openedFile = null;
        _openedLine = 0;
        _openedColumn = 0;
        Require(TypeCache.GetMethodsWithAttribute<OnOpenAssetAttribute>().Any(method =>
                method.Name == nameof(CaptureOpenAsset)),
            "The test OpenAsset callback was not indexed by the editor reflection cache.");
        Require((bool)(openAsset.Invoke(null, [sourcePath, 23, 7]) ?? false),
            "The unified path-based OpenAsset API did not report a handled source file.");
        Require(_openedFile is not null && Path.GetFullPath(_openedFile)
                    .Equals(sourcePath, StringComparison.OrdinalIgnoreCase) &&
                _openedLine == 23 && _openedColumn == 7,
            "The unified OpenAsset API did not preserve its file, line, and column arguments.");
    }

    private static List<GpuCanvasCommand> RenderStackTrace(object console, MethodInfo drawStackTrace,
        string stackTrace, Event evt)
    {
        var commands = new List<GpuCanvasCommand>();
        BeginFrame.Invoke(null, [evt, 1600, 300, commands]);
        try { drawStackTrace.Invoke(console, [stackTrace]); }
        finally { EndFrame.Invoke(null, null); }
        return commands;
    }

    private static void VerifySelectableLogText(Type consoleType)
    {
        var drawLines = consoleType.GetMethod("DrawLines", BindingFlags.Static | BindingFlags.NonPublic) ??
                        throw new MissingMethodException(consoleType.FullName, "DrawLines");
        const string text = "CONSOLE_SELECT_FIRST\nCONSOLE_SELECT_SECOND";
        var commands = RenderLogText(drawLines, text, new Event(EventType.Repaint));
        var rendered = commands.Single(command => command.Type == GpuCanvasCommandType.Text &&
                                                  command.Content == text);
        var start = new Vector2((Fix64)(rendered.Rect.X + 1), (Fix64)(rendered.Rect.Y + 2));
        var finish = new Vector2((Fix64)(rendered.Rect.Right - 1), (Fix64)(rendered.Rect.Bottom - 2));

        RenderLogText(drawLines, text,
            new Event(EventType.MouseDown) { mousePosition = start, button = 0 });
        RenderLogText(drawLines, text,
            new Event(EventType.MouseDrag)
            {
                mousePosition = finish,
                delta = finish - start,
                button = 0
            });
        RenderLogText(drawLines, text,
            new Event(EventType.MouseUp) { mousePosition = finish, button = 0 });
        GUIUtility.systemCopyBuffer = string.Empty;
        RenderLogText(drawLines, text,
            new Event(EventType.KeyDown)
            {
                modifiers = EventModifiers.Control,
                keyCode = KeyCode.C
            });

        Require(GUIUtility.systemCopyBuffer == text,
            "Console detail text could not be selected across lines and copied.");
        Require(GUIUtility.hotControl == 0,
            "Console selectable text did not release its pointer capture.");
        GUIUtility.keyboardControl = 0;
    }

    private static List<GpuCanvasCommand> RenderLogText(MethodInfo drawLines, string text, Event evt)
    {
        var commands = new List<GpuCanvasCommand>();
        BeginFrame.Invoke(null, [evt, 1600, 300, commands]);
        try { drawLines.Invoke(null, [text]); }
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

    private static T ReadMember<T>(object target, params string[] names)
    {
        foreach (var name in names)
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public |
                                                               BindingFlags.NonPublic);
            if (property?.GetValue(target) is T propertyValue) return propertyValue;
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public |
                                                        BindingFlags.NonPublic);
            if (field?.GetValue(target) is T fieldValue) return fieldValue;
        }
        throw new MissingMemberException(target.GetType().FullName, string.Join('/', names));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
