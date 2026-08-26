using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.ConsoleToolbarBehavior;

internal static class Program
{
    private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string PreferencePrefix = "BEngine.Console.";
    private static readonly Assembly EditorAssembly = typeof(EditorWindow).Assembly;
    private static readonly Type StoreType = RequireType("BEngine.Editor.EditorLogStore");
    private static readonly Type PreferencesType = RequireType("BEngine.Editor.ConsolePreferences");
    private static readonly Type ControllerType = RequireType("BEngine.Editor.ConsoleLogController");
    private static readonly Type TriggerType = RequireType("BEngine.Editor.ConsoleClearTrigger");
    private static readonly Type ApplicationType = RequireType("BEngine.Editor.GpuEditorApplication");
    private static readonly Type ConsoleType = RequireType(
        "BEngine.Editor.GpuEditorApplication+ImGuiConsoleWindow");
    private static readonly MethodInfo StoreAdd = RequireMethod(StoreType, "Add", HiddenStatic);
    private static readonly MethodInfo StoreClear = RequireMethod(StoreType, "Clear", HiddenStatic);
    private static readonly MethodInfo StoreSnapshot = RequireMethod(StoreType, "Snapshot", HiddenStatic);
    private static readonly MethodInfo ClearIfEnabled = RequireMethod(
        ControllerType, "ClearIfEnabled", HiddenStatic);
    private static readonly MethodInfo ShouldPauseOnError = RequireMethod(
        ControllerType, "ShouldPauseOnError", HiddenStatic);
    private static readonly MethodInfo FormatCount = RequireMethod(ConsoleType, "FormatCount", HiddenStatic);
    private static readonly MethodInfo BuildVisibleLogs = RequireMethod(
        ConsoleType, "BuildVisibleLogs", HiddenStatic);
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod(
        "BeginFrame", HiddenStatic) ?? throw new MissingMethodException(typeof(GUI).FullName, "BeginFrame");
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod(
        "EndFrame", HiddenStatic) ?? throw new MissingMethodException(typeof(GUI).FullName, "EndFrame");
    private static readonly FieldInfo DevicePixelsPerPoint = typeof(GUI).Assembly
        .GetType("BEngine.Editor.GUIUtility", throwOnError: true)!
        .GetField("devicePixelsPerPoint", HiddenStatic) ??
        throw new MissingFieldException("BEngine.Editor.GUIUtility", "devicePixelsPerPoint");

    private static int Main()
    {
        var preferences = CapturePreferences();
        try
        {
            EditorAppearance.Apply(new EditorPreferencesDocument());
            RequireMethod(StoreType, "Initialize", HiddenStatic).Invoke(null, null);
            VerifyCountFormatting();
            VerifyTypeCountsInToolbar();
            VerifyToolbarControls();
            VerifyToolbarButtonChrome();
            VerifyToolbarLayout();
            VerifyClearCommand();
            VerifyClearTriggers();
            VerifyLifecycleWiring();
            VerifyCollapse();
            VerifyErrorPause();
            Console.WriteLine("CONSOLE_TOOLBAR_BEHAVIOR_OK|counts,999+,clear,clear-on-play," +
                              "clear-on-build,clear-on-recompile,collapse,error-pause," +
                              "complete-toolbar-labels,textured-button-chrome,short-search,narrow-layout," +
                              "stable-narrow-resize");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            ClearStore();
            RestorePreferences(preferences);
        }
    }

    private static void VerifyCountFormatting()
    {
        Require(Format(0) == "0", "Console count formatter changed zero to a non-numeric label.");
        Require(Format(17) == "17", "Console count formatter did not preserve an exact count below 999.");
        Require(Format(999) == "999", "Console count formatter truncated 999.");
        Require(Format(1_000) == "999+", "Console count formatter must show 999+ at 1000.");
        Require(Format(int.MaxValue) == "999+", "Console count formatter overflowed for a large count.");

        var countWidth = (Fix64)(RequireMethod(ConsoleType, "CountButtonWidth", HiddenStatic)
            .Invoke(null, ["999+"]) ?? Fix64.Zero);
        var textWidth = EditorStyles.toolbarButton.CalcSize(new GUIContent("999+")).x;
        Require(countWidth > textWidth,
            "Console count button is too narrow to display the complete 999+ label.");

        var onGui = RequireMethod(ConsoleType, "OnGUI", HiddenInstance);
        Require(ContainsCall(onGui, FormatCount),
            "Console toolbar does not use its bounded count formatter when drawing category totals.");
    }

    private static void VerifyTypeCountsInToolbar()
    {
        ClearStore();
        AddLogs(LogType.Info, 7, "TYPE_INFO");
        AddLogs(LogType.Warning, 11, "TYPE_WARNING");
        AddLogs(LogType.Error, 13, "TYPE_ERROR");
        SetPreference("Collapse", false);

        var text = RenderConsole().Where(command => command.Type == GpuCanvasCommandType.Text)
            .Select(command => command.Content).ToArray();
        Require(text.Contains("7", StringComparer.Ordinal), "Console did not display the exact Info count.");
        Require(text.Contains("11", StringComparer.Ordinal), "Console did not display the exact Warning count.");
        Require(text.Contains("13", StringComparer.Ordinal), "Console did not display the exact Error count.");

        ClearStore();
        AddLogs(LogType.Info, 1_000, "COUNT_LIMIT");
        text = RenderConsole().Where(command => command.Type == GpuCanvasCommandType.Text)
            .Select(command => command.Content).ToArray();
        Require(text.Contains("999+", StringComparer.Ordinal),
            "Console toolbar did not apply the 999+ limit to the rendered count.");
    }

    private static void VerifyToolbarControls()
    {
        ClearStore();
        var text = RenderConsole().Where(command => command.Type == GpuCanvasCommandType.Text)
            .Select(command => command.Content).ToArray();
        Require(text.Contains("Clear", StringComparer.Ordinal), "Console toolbar has no visible Clear command.");
        Require(text.Contains("Collapse", StringComparer.Ordinal), "Console toolbar has no Collapse toggle.");
        Require(text.Contains("Error Pause", StringComparer.Ordinal), "Console toolbar has no Error Pause toggle.");

        var options = RequireMethod(ConsoleType, "ShowClearOptionsMenu", HiddenInstance);
        foreach (var name in new[] { "ClearOnPlay", "ClearOnBuild", "ClearOnRecompile" })
        {
            var getter = PreferencesType.GetProperty(name, HiddenStatic)?.GetMethod ??
                         throw new MissingMethodException(PreferencesType.FullName, $"get_{name}");
            Require(ContainsCall(options, getter),
                $"Console clear-options menu is not connected to {name}.");
        }
    }

    private static void VerifyToolbarButtonChrome()
    {
        var original = new EditorPreferencesDocument
        {
            EditorTheme = EditorAppearance.theme.ToString(),
            EditorFontSize = EditorAppearance.fontSize,
            EditorFont = EditorAppearance.fontFamily
        };
        try
        {
            foreach (var theme in Enum.GetNames<EditorTheme>())
            {
                EditorAppearance.Apply(new EditorPreferencesDocument { EditorTheme = theme });
                var palette = EditorAppearance.palette;
                foreach (var style in new[] { EditorStyles.toolbarButton, EditorStyles.toolbarIconButton })
                {
                    Require(style.normal.backgroundColor.Equals(palette.Toolbar),
                        $"{theme} toolbar button normal color was not restored.");
                    Require(style.borderWidth == Fix64.Zero &&
                            style.normal.borderColor.Equals(palette.Border) &&
                            style.normal.backgroundImage is not null,
                        $"{theme} toolbar buttons have no textured separator background.");
                    Require(!style.hover.backgroundColor.Equals(style.normal.backgroundColor) &&
                            !style.active.backgroundColor.Equals(style.normal.backgroundColor),
                        $"{theme} toolbar buttons lost their hover or pressed feedback.");
                    Require(style.disabled.backgroundColor.Equals(style.normal.backgroundColor) &&
                            style.disabled.textColor.Equals(palette.DisabledText),
                        $"{theme} disabled toolbar buttons do not preserve their surface and muted text.");
                }

                var selected = EditorStyles.toolbarIconButtonSelected;
                Require(selected.normal.backgroundColor.Equals(palette.Selection) &&
                        selected.normal.borderColor.Equals(palette.Border) &&
                        selected.borderWidth == Fix64.Zero && selected.normal.backgroundImage is not null,
                    $"{theme} selected toolbar buttons lost their selected surface or boundary.");
            }
        }
        finally
        {
            EditorAppearance.Apply(original);
        }

        var commands = RenderConsole();
        var separator = GpuCanvasColor.FromColor(EditorAppearance.palette.Border);
        foreach (var label in new[] { "Clear", "Collapse", "Error Pause" })
        {
            var text = commands.Single(command => command.Type == GpuCanvasCommandType.Text &&
                                                  command.Content == label && command.Rect.Y < 50);
            Require(commands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                            command.Color == separator &&
                                            Math.Abs(command.Rect.Width - 1) < .01f &&
                                            command.Rect.X >= text.Rect.Right &&
                                            command.Rect.X - text.Rect.Right <= 16 &&
                                            command.Rect.Y <= text.Rect.Y &&
                                            command.Rect.Bottom >= text.Rect.Bottom),
                $"Console toolbar command '{label}' rendered without a vertical texture separator.");
        }
    }

    private static void VerifyToolbarLayout()
    {
        VerifyToolbarTextFitsAtFixedFont();
        VerifyNarrowToolbarCounts();
        VerifyNarrowToolbarResizeStability();
    }

    private static void VerifyToolbarTextFitsAtFixedFont()
    {
        const string searchProbe = "console probe";
        var defaultAppearance = new EditorPreferencesDocument();
        try
        {
            EditorAppearance.Apply(new EditorPreferencesDocument { EditorFontSize = 20 });
            Require(EditorAppearance.fontSize == EditorAppearance.DefaultFontSize,
                "Console accepted a variable editor font size.");
            ClearStore();
            var commands = RenderConsole(1_200, 240, console =>
            {
                var searchField = ConsoleType.GetField("_search", HiddenInstance) ??
                                  throw new MissingFieldException(ConsoleType.FullName, "_search");
                searchField.SetValue(console, searchProbe);
            });
            var toolbarText = commands.Where(command => command.Type == GpuCanvasCommandType.Text &&
                                                        command.Rect.Y < 50).ToArray();
            foreach (var label in new[] { "Clear", "Collapse", "Error Pause" })
            {
                var command = toolbarText.SingleOrDefault(item => item.Content == label);
                Require(command.Content == label,
                    $"Console toolbar did not render the complete {label} label at the fixed font size.");
                RequireTextFits(command, label);
            }

            var searchText = toolbarText.SingleOrDefault(item => item.Content == searchProbe);
            Require(searchText.Content == searchProbe,
                "Console search field probe was not rendered.");
            RequireTextFits(searchText, searchProbe, EditorStyles.toolbarSearchField);
            var searchIcon = commands.Where(command => command.Type == GpuCanvasCommandType.Image &&
                                                        command.Content == EditorBuiltinIcons.Toolbar.Search &&
                                                        command.Rect.Y < 50 &&
                                                        command.Rect.X < searchText.Rect.X)
                .OrderByDescending(command => command.Rect.X).FirstOrDefault();
            var searchClear = commands.Where(command => command.Type == GpuCanvasCommandType.Image &&
                                                         command.Content == EditorBuiltinIcons.Toolbar.Clear &&
                                                         command.Rect.Y < 50 &&
                                                         command.Rect.X > searchText.Rect.X)
                .OrderBy(command => command.Rect.X).FirstOrDefault();
            Require(searchIcon.Content == EditorBuiltinIcons.Toolbar.Search &&
                    searchClear.Content == EditorBuiltinIcons.Toolbar.Clear,
                "Console search field chrome was not rendered around the search text.");
            Require(searchClear.Rect.Right - searchIcon.Rect.X <= 220.5f,
                $"Console search field is wider than 220px: " +
                $"{searchClear.Rect.Right - searchIcon.Rect.X:0.##}px.");
        }
        finally
        {
            EditorAppearance.Apply(defaultAppearance);
        }
    }

    private static void VerifyNarrowToolbarCounts()
    {
        ClearStore();
        AddLogs(LogType.Info, 1_000, "NARROW_INFO");
        AddLogs(LogType.Warning, 1_000, "NARROW_WARNING");
        AddLogs(LogType.Error, 1_000, "NARROW_ERROR");
        var labels = RenderConsole(520, 220)
            .Where(command => command.Type == GpuCanvasCommandType.Text &&
                              command.Content == "999+" && command.Rect.Y < 50)
            .OrderBy(command => command.Rect.X).ToArray();
        Require(labels.Length == 3,
            $"A 520px Console toolbar must show all three 999+ category counts; rendered {labels.Length}.");
        foreach (var label in labels)
        {
            RequireTextFits(label, "999+");
            Require(label.Rect.X >= -0.1f && label.Rect.Right <= 520.1f,
                "A Console category count escaped the narrow toolbar viewport.");
        }
        for (var index = 1; index < labels.Length; index++)
            Require(labels[index - 1].Rect.Right <= labels[index].Rect.X + 0.1f,
                "Console category counts overlap at a 520px window width.");
    }

    private static void VerifyNarrowToolbarResizeStability()
    {
        const string searchProbe = "resize probe";
        ClearStore();
        var console = CreateConsole();
        (ConsoleType.GetField("_search", HiddenInstance) ??
         throw new MissingFieldException(ConsoleType.FullName, "_search")).SetValue(console, searchProbe);
        var previousDeviceScale = (Fix64)(DevicePixelsPerPoint.GetValue(null) ?? Fix64.Zero);
        var signatures = new Dictionary<(int Width, decimal Scale), string>();
        string[]? expectedContents = null;
        (int Width, decimal Scale)[] frames =
        [
            (520, 1.0000m), (519, 1.0000m), (518, 1.0000m), (517, 1.0000m),
            (518, 1.0000m), (519, 1.0000m), (520, 1.0000m),
            (520, 1.0005m), (520, 1.0009m), (520, 1.0005m), (520, 1.0000m)
        ];

        try
        {
            DevicePixelsPerPoint.SetValue(null, Fix64.One);
            Require(HasModeButtons(RenderConsoleResizeFrame(console, 800, 220)),
                "Console mode buttons were not available before testing narrow-window hysteresis.");
            var hideAt = Enumerable.Range(300, 501).Reverse().First(width =>
                !HasModeButtons(RenderConsoleResizeFrame(console, width, 220)));
            var showAt = Enumerable.Range(hideAt, 801 - hideAt).First(width =>
                HasModeButtons(RenderConsoleResizeFrame(console, width, 220)));
            Require(showAt - hideAt >= 8,
                $"Console mode-button visibility has no resize hysteresis: hide={hideAt}, show={showAt}.");
            var hysteresisMiddle = (hideAt + showAt) / 2;
            foreach (var width in new[] { hysteresisMiddle, hysteresisMiddle - 1, hysteresisMiddle + 1,
                         hysteresisMiddle, hysteresisMiddle + 1, hysteresisMiddle - 1 })
                Require(HasModeButtons(RenderConsoleResizeFrame(console, width, 220)),
                    $"Console mode-button text flickered off inside the visible hysteresis band at {width}px.");
            Require(!HasModeButtons(RenderConsoleResizeFrame(console, hideAt, 220)),
                "Console mode buttons did not leave the visible state at the measured hide threshold.");
            foreach (var width in new[] { hysteresisMiddle, hysteresisMiddle + 1, hysteresisMiddle - 1,
                         hysteresisMiddle, hysteresisMiddle - 1, hysteresisMiddle + 1 })
                Require(!HasModeButtons(RenderConsoleResizeFrame(console, width, 220)),
                    $"Console mode-button text flickered on inside the hidden hysteresis band at {width}px.");

            AddLogs(LogType.Info, 1_000, "RESIZE_INFO");
            AddLogs(LogType.Warning, 1_000, "RESIZE_WARNING");
            AddLogs(LogType.Error, 1_000, "RESIZE_ERROR");
            foreach (var frame in frames)
            {
                DevicePixelsPerPoint.SetValue(null, Fix64.FromDecimal(frame.Scale));
                var commands = RenderConsoleResizeFrame(console, frame.Width, 220);
                var toolbarText = commands.Where(command => command.Type == GpuCanvasCommandType.Text &&
                                                            command.Rect.Y < 50)
                    .OrderBy(command => command.Rect.X).ThenBy(command => command.Content,
                        StringComparer.Ordinal).ToArray();
                var contents = toolbarText.Select(command => command.Content).ToArray();
                expectedContents ??= contents;
                Require(contents.SequenceEqual(expectedContents, StringComparer.Ordinal),
                    $"Console toolbar text appeared or disappeared while narrowly resizing at " +
                    $"{frame.Width}px/{frame.Scale:0.0000}x: {string.Join(", ", contents)}.");
                Require(toolbarText.Count(command => command.Content == "999+") == 3,
                    "A Console log count disappeared during a narrow resize sequence.");
                Require(toolbarText.Count(command => command.Content == searchProbe) == 1,
                    "Console search text flickered during a narrow resize sequence.");
                foreach (var command in toolbarText)
                    Require(command.Rect.Width > 0 && float.IsFinite(command.Rect.X) &&
                            float.IsFinite(command.Rect.Width) && command.Rect.X >= command.ClipRect.X - 0.1f &&
                            command.Rect.Right <= command.ClipRect.Right + 0.1f,
                        $"Console toolbar text '{command.Content}' became invalid or escaped its clip while resizing.");

                var signature = CommandSignature(toolbarText);
                if (signatures.TryGetValue(frame, out var previous))
                    Require(signature == previous,
                        $"Console toolbar emitted different text commands when returning to " +
                        $"{frame.Width}px/{frame.Scale:0.0000}x.");
                else signatures.Add(frame, signature);
            }
        }
        finally
        {
            DevicePixelsPerPoint.SetValue(null, previousDeviceScale);
        }
    }

    private static bool HasModeButtons(IEnumerable<GpuCanvasCommand> commands)
    {
        var labels = commands.Where(command => command.Type == GpuCanvasCommandType.Text && command.Rect.Y < 50)
            .Select(command => command.Content).ToArray();
        return labels.Contains("Collapse", StringComparer.Ordinal) &&
               labels.Contains("Error Pause", StringComparer.Ordinal);
    }

    private static string CommandSignature(IEnumerable<GpuCanvasCommand> commands) => string.Join("|",
        commands.Select(command => string.Join(":",
            command.Content,
            BitConverter.SingleToInt32Bits(command.Rect.X),
            BitConverter.SingleToInt32Bits(command.Rect.Y),
            BitConverter.SingleToInt32Bits(command.Rect.Width),
            BitConverter.SingleToInt32Bits(command.Rect.Height),
            BitConverter.SingleToInt32Bits(command.ClipRect.X),
            BitConverter.SingleToInt32Bits(command.ClipRect.Width))));

    private static void RequireTextFits(GpuCanvasCommand command, string label, GUIStyle? style = null)
    {
        var requiredWidth = (float)(style ?? EditorStyles.toolbarIconButton)
            .CalcSize(new GUIContent(label)).x;
        Require(command.Rect.Width + 0.1f >= requiredWidth,
            $"Console toolbar label '{label}' is clipped: " +
            $"rendered={command.Rect.Width:0.##}, required={requiredWidth:0.##}.");
        Require(command.Rect.X >= command.ClipRect.X - 0.1f &&
                command.Rect.X + requiredWidth <= command.ClipRect.Right + 0.1f,
            $"Console toolbar label '{label}' falls outside its clip rectangle.");
    }

    private static void VerifyClearCommand()
    {
        ClearStore();
        var selected = Entry(LogType.Warning, "SELECTED");
        Add(selected);
        Add(Entry(LogType.Info, "SECOND"));
        var console = CreateConsole();
        var selectedField = ConsoleType.GetField("_selected", HiddenInstance) ??
                            throw new MissingFieldException(ConsoleType.FullName, "_selected");
        selectedField.SetValue(console, selected);
        RequireMethod(ConsoleType, "Clear", HiddenInstance).Invoke(console, null);
        Require(Snapshot().Length == 0, "Console Clear did not remove all stored logs.");
        Require(selectedField.GetValue(console) is null, "Console Clear retained a stale selected log.");
    }

    private static void VerifyClearTriggers()
    {
        VerifyClearTrigger("Play", "ClearOnPlay");
        VerifyClearTrigger("Build", "ClearOnBuild");
        VerifyClearTrigger("Recompile", "ClearOnRecompile");
    }

    private static void VerifyClearTrigger(string triggerName, string preferenceName)
    {
        var trigger = Enum.Parse(TriggerType, triggerName);
        SetPreference(preferenceName, false);
        ClearStore();
        Add(Entry(LogType.Info, $"{triggerName}_DISABLED"));
        Require(!(bool)(ClearIfEnabled.Invoke(null, [trigger]) ?? true),
            $"{preferenceName}=false reported that it cleared the Console.");
        Require(Snapshot().Length == 1,
            $"{preferenceName}=false still cleared the Console.");

        SetPreference(preferenceName, true);
        Require((bool)(ClearIfEnabled.Invoke(null, [trigger]) ?? false),
            $"{preferenceName}=true did not report that it cleared the Console.");
        Require(Snapshot().Length == 0,
            $"{preferenceName}=true did not clear the Console.");
    }

    private static void VerifyLifecycleWiring()
    {
        Require(ContainsCall(RequireMethod(ApplicationType, "EnterPlayModeCore", HiddenInstance), ClearIfEnabled),
            "Entering Play Mode is not wired to the Console clear controller.");
        Require(ContainsCall(RequireMethod(ApplicationType, "CompileScripts", HiddenInstance), ClearIfEnabled),
            "Script recompilation is not wired to the Console clear controller.");

        var buildPipeline = RequireType("BEngine.Editor.BuildPipeline");
        var raiseBuildStarted = RequireMethod(buildPipeline, "RaiseBuildStarted", HiddenStatic);
        Require(ContainsCall(raiseBuildStarted, ClearIfEnabled),
            "Player build start is not wired to the Console clear controller.");

        SetPreference("ClearOnBuild", true);
        ClearStore();
        Add(Entry(LogType.Info, "BUILD_STARTED"));
        raiseBuildStarted.Invoke(null, [Path.GetFullPath("Build")]);
        Require(Snapshot().Length == 0, "The actual build-start lifecycle did not clear the Console.");
    }

    private static void VerifyCollapse()
    {
        var first = new LogEntry(new DateTimeOffset(2026, 8, 18, 1, 0, 0, TimeSpan.Zero),
            LogType.Info, "REPEATED", "same stack");
        var second = first with { Timestamp = first.Timestamp.AddSeconds(1) };
        var third = first with { Timestamp = first.Timestamp.AddSeconds(2) };
        var differentType = first with { Type = LogType.Warning };
        var differentStack = first with { StackTrace = "different stack" };
        LogEntry[] source = [first, second, third, differentType, differentStack];

        var expanded = InvokeVisibleLogs(source, false);
        Require(expanded.Length == source.Length,
            "Collapse=false removed Console rows.");
        Require(expanded.All(item => ReadMember<int>(item, "Count") == 1),
            "Expanded Console rows have an invalid repetition count.");

        var collapsed = InvokeVisibleLogs(source, true);
        Require(collapsed.Length == 3,
            "Collapse did not group logs by type, message, and stack trace while ignoring timestamp.");
        var repeated = collapsed.Single(item =>
            ReadMember<LogEntry>(item, "Entry").Type == LogType.Info &&
            ReadMember<LogEntry>(item, "Entry").Message == "REPEATED" &&
            ReadMember<LogEntry>(item, "Entry").StackTrace == "same stack");
        Require(ReadMember<int>(repeated, "Count") == 3,
            "Collapsed Console row did not retain its exact repetition count.");

        var refreshLogCache = RequireMethod(ConsoleType, "RefreshLogCache", HiddenInstance);
        Require(ContainsCall(refreshLogCache, BuildVisibleLogs),
            "Console log cache does not use the collapse aggregation path.");
    }

    private static void VerifyErrorPause()
    {
        var error = Entry(LogType.Error, "PAUSE_ERROR");
        var warning = Entry(LogType.Warning, "NO_PAUSE_WARNING");
        SetPreference("ErrorPause", false);
        Require(!ShouldPause(error, true), "Error Pause disabled still paused Play Mode.");
        SetPreference("ErrorPause", true);
        Require(!ShouldPause(error, false), "An editor-time error paused outside Play Mode.");
        Require(!ShouldPause(warning, true), "Error Pause treated a warning as an error.");
        Require(ShouldPause(error, true), "Error Pause did not pause for an error during Play Mode.");

        var onLog = RequireMethod(ApplicationType, "OnLog", HiddenInstance);
        Require(ContainsCall(onLog, ShouldPauseOnError),
            "The editor log callback is not wired to Error Pause behavior.");
    }

    private static object[] InvokeVisibleLogs(IReadOnlyList<LogEntry> logs, bool collapse) =>
        ((IEnumerable)(BuildVisibleLogs.Invoke(null, [logs, collapse]) ??
                       throw new InvalidOperationException("BuildVisibleLogs returned null.")))
        .Cast<object>().ToArray();

    private static bool ShouldPause(LogEntry entry, bool playing) =>
        (bool)(ShouldPauseOnError.Invoke(null, [entry, playing]) ?? false);

    private static string Format(int count) =>
        (string)(FormatCount.Invoke(null, [count]) ?? throw new InvalidOperationException("FormatCount returned null."));

    private static void AddLogs(LogType type, int count, string message)
    {
        for (var index = 0; index < count; index++)
            Add(new LogEntry(DateTimeOffset.UnixEpoch.AddMilliseconds(index), type, message, string.Empty));
    }

    private static List<GpuCanvasCommand> RenderConsole(int width = 1_200, int height = 720,
        Action<object>? configure = null)
    {
        var console = CreateConsole();
        configure?.Invoke(console);
        return RenderConsoleFrame(console, width, height);
    }

    private static List<GpuCanvasCommand> RenderConsoleFrame(object console, int width, int height)
    {
        var commands = new List<GpuCanvasCommand>();
        BeginFrame.Invoke(null, [new Event(EventType.Repaint), width, height, commands]);
        try { RequireMethod(ConsoleType, "OnGUI", HiddenInstance).Invoke(console, null); }
        finally { EndFrame.Invoke(null, null); }
        return commands;
    }

    private static List<GpuCanvasCommand> RenderConsoleResizeFrame(object console, int width, int height)
    {
        BeginFrame.Invoke(null, [new Event(EventType.Layout), width, height, new List<GpuCanvasCommand>()]);
        try { RequireMethod(ConsoleType, "OnGUI", HiddenInstance).Invoke(console, null); }
        finally { EndFrame.Invoke(null, null); }
        return RenderConsoleFrame(console, width, height);
    }

    private static object CreateConsole()
    {
        var app = RuntimeHelpers.GetUninitializedObject(ApplicationType);
        var constructor = ConsoleType.GetConstructor(BindingFlags.Instance | BindingFlags.Public |
                                                     BindingFlags.NonPublic, null, [ApplicationType], null) ??
                          throw new MissingMethodException(ConsoleType.FullName, ".ctor(GpuEditorApplication)");
        return constructor.Invoke([app]);
    }

    private static bool ContainsCall(MethodBase caller, MethodBase target)
    {
        var body = caller.GetMethodBody()?.GetILAsByteArray() ?? [];
        var token = BitConverter.GetBytes(target.MetadataToken);
        for (var index = 0; index <= body.Length - token.Length; index++)
            if (body.AsSpan(index, token.Length).SequenceEqual(token)) return true;
        return false;
    }

    private static LogEntry Entry(LogType type, string message) =>
        new(DateTimeOffset.UtcNow, type, message, $"at {nameof(Program)}.{nameof(Entry)}()");

    private static void Add(LogEntry entry) => StoreAdd.Invoke(null, [entry]);
    private static void ClearStore() => StoreClear.Invoke(null, null);
    private static LogEntry[] Snapshot() =>
        (LogEntry[])(StoreSnapshot.Invoke(null, null) ?? Array.Empty<LogEntry>());

    private static PreferenceState[] CapturePreferences() =>
        new[] { "Collapse", "ErrorPause", "ClearOnPlay", "ClearOnBuild", "ClearOnRecompile" }
            .Select(name =>
            {
                var key = PreferencePrefix + name;
                return new PreferenceState(key, EditorPrefs.HasKey(key), EditorPrefs.GetBool(key));
            }).ToArray();

    private static void RestorePreferences(IEnumerable<PreferenceState> preferences)
    {
        foreach (var preference in preferences)
        {
            if (preference.Existed) EditorPrefs.SetBool(preference.Key, preference.Value);
            else EditorPrefs.DeleteKey(preference.Key);
        }
    }

    private static void SetPreference(string name, bool value)
    {
        var property = PreferencesType.GetProperty(name, HiddenStatic) ??
                       throw new MissingMemberException(PreferencesType.FullName, name);
        Require(property.PropertyType == typeof(bool) && property.CanRead && property.CanWrite,
            $"Console preference {name} is not a readable/writable bool.");
        property.SetValue(null, value);
        Require((bool)(property.GetValue(null) ?? !value) == value,
            $"Console preference {name} did not retain its value.");
    }

    private static T ReadMember<T>(object target, string name)
    {
        var type = target.GetType();
        if (type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(target) is T propertyValue) return propertyValue;
        if (type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(target) is T fieldValue) return fieldValue;
        throw new MissingMemberException(type.FullName, name);
    }

    private static Type RequireType(string name) => EditorAssembly.GetType(name, throwOnError: true)!;

    private static MethodInfo RequireMethod(Type type, string name, BindingFlags flags) =>
        type.GetMethod(name, flags) ?? throw new MissingMethodException(type.FullName, name);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private readonly record struct PreferenceState(string Key, bool Existed, bool Value);
}
