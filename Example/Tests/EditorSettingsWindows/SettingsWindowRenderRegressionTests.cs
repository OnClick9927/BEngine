using System.Reflection;
using BEngine;
using BEngine.Editor;
using BEngine.Editor.Documents;
using BEngine.Editor.Rendering;
using BEngine.Documents;

namespace BEngine.ExampleTests.EditorSettingsWindows;

internal static class SettingsWindowRenderRegressionTests
{
    private const int WideWidth = 900;
    private const int WideHeight = 620;
    private const int NarrowWidth = 360;
    private const int NarrowHeight = 440;
    private static readonly MethodInfo BeginFrame = typeof(GUI).GetMethod(
        "BeginFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo EndFrame = typeof(GUI).GetMethod(
        "EndFrame", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo OpenWindow = typeof(EditorWindow).GetMethod(
        "OpenInternal", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo CloseWindow = typeof(EditorWindow).GetMethod(
        "CloseInternal", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo DrawWindow = typeof(EditorWindow).GetMethod(
        "OnGUIInternal", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo SelectPath = typeof(SettingsWindowBase).GetMethod(
        "SelectPath", BindingFlags.Instance | BindingFlags.NonPublic)!;

    internal static IReadOnlyList<string> Run(string testDirectory, string projectSettingsPath)
    {
        var markers = new List<string>();
        var previousScale = GUIUtility.pixelsPerPoint;
        GUIUtility.pixelsPerPoint = Fix64.One;
        GUIUtility.keyboardControl = 0;
        GUIUtility.hotControl = 0;
        SettingsWindowRegressionState.Reset();
        SettingsProviderRegistry.Invalidate();
        try
        {
            VerifyPreferencesWindow(testDirectory);
            markers.AddRange(["preferences-render", "selection", "search", "navigation-scroll",
                "content-scroll", "narrow", "multi-frame", "provider-fault", "preferences-persist"]);
            VerifyProjectSettingsWindow(projectSettingsPath);
            markers.AddRange(["project-render", "tag-layer-tabs", "apply-persist"]);
            return markers;
        }
        finally
        {
            GUIUtility.keyboardControl = 0;
            GUIUtility.hotControl = 0;
            GUIUtility.pixelsPerPoint = previousScale;
        }
    }

    private static void VerifyPreferencesWindow(string testDirectory)
    {
        EditorPreferences.Initialize();
        EditorPreferences.current.Locale = "en-US";
        EditorPreferences.current.AutoRefreshAssets = true;
        EditorPreferences.Save();
        SettingsProviderRegistry.Invalidate();

        var window = new PreferencesWindow();
        Open(window);
        try
        {
            Select(window, SettingsWindowRegressionState.UserOverviewPath);
            _ = Render(window, WideWidth, WideHeight);
            var first = Render(window, WideWidth, WideHeight);
            VerifyTwoPaneLayout(first, WideWidth, WideHeight, "User Overview",
                SettingsWindowRegressionState.UserBodyMarker);
            var second = Render(window, WideWidth, WideHeight);
            Require(first.SequenceEqual(second),
                "Preferences emitted different GPU commands across unchanged repaint frames.");

            VerifyContentScroll(window, first, SettingsWindowRegressionState.UserRowPrefix,
                SettingsWindowRegressionState.UserFooterMarker);
            VerifySelectionAndFaultIsolation(window, "User Secondary",
                SettingsWindowRegressionState.UserSecondaryMarker, "User Fault", "User Overview",
                SettingsWindowRegressionState.UserBodyMarker);
            VerifySearchAndNavigationScroll(window);
            VerifyLargeFontLayout(window);

            Select(window, SettingsWindowRegressionState.UserSecondaryPath);
            Select(window, SettingsWindowRegressionState.UserOverviewPath);
            _ = Render(window, NarrowWidth, NarrowHeight);
            var narrow = Render(window, NarrowWidth, NarrowHeight);
            VerifyTwoPaneLayout(narrow, NarrowWidth, NarrowHeight, "User Overview",
                SettingsWindowRegressionState.UserBodyMarker);
            Require(narrow.SequenceEqual(Render(window, NarrowWidth, NarrowHeight)),
                "Narrow Preferences layout changed between unchanged repaint frames.");

            Select(window, "Preferences/General");
            var general = Render(window, WideWidth, WideHeight);
            var toggleLabel = FindVisibleText(general, "Auto Refresh Assets");
            var togglePoint = new Vector2((Fix64)(toggleLabel.Rect.Right + 12),
                (Fix64)(toggleLabel.Rect.Y + toggleLabel.Rect.Height / 2));
            Click(window, WideWidth, WideHeight, togglePoint);
            Require(!EditorPreferences.current.AutoRefreshAssets,
                "Preferences toggle did not apply through the real IMGUI event path.");
            var persisted = Document.Load<EditorPreferencesDocument>(EditorDataPaths.preferencesPath);
            Require(!persisted.AutoRefreshAssets && persisted.Locale == "en-US" &&
                    Path.GetFullPath(EditorDataPaths.preferencesPath).StartsWith(
                        Path.GetFullPath(testDirectory), StringComparison.OrdinalIgnoreCase),
                "Preferences change was not persisted to the isolated editor data document.");
        }
        finally
        {
            Close(window);
        }
    }

    private static void VerifyProjectSettingsWindow(string projectSettingsPath)
    {
        var window = new ProjectSettingsWindow();
        Open(window);
        try
        {
            Select(window, SettingsWindowRegressionState.ProjectOverviewPath);
            var commands = Render(window, WideWidth, WideHeight);
            VerifyTwoPaneLayout(commands, WideWidth, WideHeight, "Project Overview",
                SettingsWindowRegressionState.ProjectBodyMarker);
            VerifySelectionAndFaultIsolation(window, "Project Secondary",
                SettingsWindowRegressionState.ProjectSecondaryMarker, "Project Fault", "Project Overview",
                SettingsWindowRegressionState.ProjectBodyMarker);

            Select(window, SettingsWindowRegressionState.ProjectOverviewPath);
            VerifyTwoPaneLayout(Render(window, NarrowWidth, NarrowHeight), NarrowWidth, NarrowHeight,
                "Project Overview", SettingsWindowRegressionState.ProjectBodyMarker);

            VerifyTagLayerTabs(window);

            EditorProjectSettings.current.ScriptingDefineSymbols = ["OLD_SYMBOL"];
            EditorProjectSettings.Save();
            Select(window, "Project/Scripting");
            var scripting = Render(window, WideWidth, WideHeight);
            var oldValue = FindVisibleText(scripting, "OLD_SYMBOL");
            DoubleClick(window, WideWidth, WideHeight, Center(oldValue.Rect));
            TypeText(window, WideWidth, WideHeight, "RENDER_REGRESSION");
            var edited = Render(window, WideWidth, WideHeight);
            Require(HasVisibleText(edited, "RENDER_REGRESSION"),
                "Scripting symbols text field did not retain typed input.");
            ClickText(window, WideWidth, WideHeight, edited, "Apply");
            var persisted = Document.Load<ProjectSettingsDocument>(projectSettingsPath);
            Require(persisted.ScriptingDefineSymbols.SequenceEqual(["RENDER_REGRESSION"]),
                "Project Settings Apply did not persist scripting symbols.");
        }
        finally
        {
            Close(window);
        }
    }

    private static void VerifyTagLayerTabs(ProjectSettingsWindow window)
    {
        Select(window, "Project/Tags and Layers");
        var tags = Render(window, WideWidth, WideHeight);
        Require(HasVisibleText(tags, "Tags") && HasVisibleText(tags, "Layers") &&
                HasVisibleText(tags, "Untagged") && HasVisibleText(tags, "Add Tag"),
            "The unified Tags and Layers provider did not render its Tags page and both tabs.");

        ClickText(window, WideWidth, WideHeight, tags, "Layers");
        var layers = Render(window, WideWidth, WideHeight);
        Require(HasVisibleText(layers, "Tags") && HasVisibleText(layers, "Layers") &&
                HasVisibleText(layers, "World: 2^1 - 2^58    UI: 2^59 - 2^63") &&
                HasVisibleText(layers, "2^1"),
            "Selecting the Layers tab did not render the layer editor.");

        ClickText(window, WideWidth, WideHeight, layers, "Tags");
        Require(HasVisibleText(Render(window, WideWidth, WideHeight), "Add Tag"),
            "Selecting the Tags tab did not restore the tag editor.");
    }

    private static void VerifyContentScroll(
        EditorWindow window,
        IReadOnlyList<GpuCanvasCommand> initial,
        string rowPrefix,
        string footerMarker)
    {
        var firstRow = FindText(initial, rowPrefix + "00");
        var footer = FindVisibleText(initial, footerMarker);
        Require(!HasVisibleText(initial, rowPrefix + "47"),
            "Long settings page unexpectedly exposed its last row before scrolling.");
        var viewportPoint = new Vector2((Fix64)(firstRow.ClipRect.X + firstRow.ClipRect.Width / 2),
            (Fix64)(firstRow.ClipRect.Y + firstRow.ClipRect.Height / 2));
        var consumed = false;
        for (var index = 0; index < 14; index++)
        {
            var wheel = new Event(EventType.ScrollWheel)
            {
                mousePosition = viewportPoint,
                delta = new Vector2(0, 4)
            };
            Dispatch(window, WideWidth, WideHeight, wheel, []);
            consumed |= wheel.type == EventType.Used;
        }
        var scrolled = Render(window, WideWidth, WideHeight);
        Require(consumed, "Settings content viewport did not consume the scroll wheel.");
        Require(HasVisibleText(scrolled, rowPrefix + "47"),
            "Scrolling a long settings page did not reveal its final row.");
        var scrolledFooter = FindVisibleText(scrolled, footerMarker);
        Require(Math.Abs(scrolledFooter.Rect.Y - footer.Rect.Y) < 0.01f,
            "Settings footer moved with the scrollable provider content.");
    }

    private static void VerifySelectionAndFaultIsolation(
        EditorWindow window,
        string secondaryLabel,
        string secondaryMarker,
        string faultLabel,
        string healthyLabel,
        string healthyMarker)
    {
        var commands = Render(window, WideWidth, WideHeight);
        Require(HasSelectedRowBackground(commands, healthyLabel),
            $"Selected settings row '{healthyLabel}' was painted over by its normal button background.");
        ClickText(window, WideWidth, WideHeight, commands, secondaryLabel, leftmost: true);
        var secondary = Render(window, WideWidth, WideHeight);
        Require(HasVisibleText(secondary, secondaryMarker),
            $"Selecting '{secondaryLabel}' did not render its provider body.");
        Require(HasSelectedRowBackground(secondary, secondaryLabel) &&
                !HasSelectedRowBackground(secondary, healthyLabel),
            "Settings navigation did not move its visible selection background to the newly selected row.");

        ClickText(window, WideWidth, WideHeight, secondary, faultLabel, leftmost: true);
        var failed = Render(window, WideWidth, WideHeight);
        Require(failed.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                      command.Content.Contains("failed", StringComparison.OrdinalIgnoreCase) &&
                                      IsVisible(command)),
            "A faulting SettingsProvider did not render an isolated error HelpBox.");
        Require(!HasVisibleText(failed, SettingsWindowRegressionState.FaultPartialMarker),
            "A faulting SettingsProvider leaked partial GPU commands into its error page.");

        ClickText(window, WideWidth, WideHeight, failed, healthyLabel, leftmost: true);
        var recovered = Render(window, WideWidth, WideHeight);
        Require(HasVisibleText(recovered, healthyMarker),
            "A healthy provider did not recover after another SettingsProvider corrupted IMGUI state.");
        Require(SettingsWindowRegressionState.Deactivations.Count > 0 &&
                SettingsWindowRegressionState.Activations.Count > 1,
            "Settings provider activation/deactivation lifecycle did not follow selection changes.");
    }

    private static void VerifySearchAndNavigationScroll(EditorWindow window)
    {
        Select(window, SettingsWindowRegressionState.UserOverviewPath);
        Click(window, WideWidth, WideHeight, new Vector2(40, 20));
        TypeText(window, WideWidth, WideHeight, "needle16");
        var filtered = Render(window, WideWidth, WideHeight);
        Require(HasVisibleText(filtered, "User Scroll 16") &&
                !filtered.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                         command.Content == "User Overview" &&
                                         IsNavigationCommand(command)),
            "Settings search did not filter navigation by provider keywords.");

        ClearFocusedText(window, WideWidth, WideHeight);
        var unfiltered = Render(window, WideWidth, WideHeight);
        var lastBefore = FindText(unfiltered, "User Scroll 16", leftmost: true);
        var navigationPoint = new Vector2((Fix64)(lastBefore.ClipRect.X + lastBefore.ClipRect.Width / 2),
            (Fix64)(lastBefore.ClipRect.Y + lastBefore.ClipRect.Height / 2));
        var consumed = false;
        for (var index = 0; index < 8; index++)
        {
            var wheel = new Event(EventType.ScrollWheel)
            {
                mousePosition = navigationPoint,
                delta = new Vector2(0, 5)
            };
            Dispatch(window, WideWidth, WideHeight, wheel, []);
            consumed |= wheel.type == EventType.Used;
        }
        var scrolled = Render(window, WideWidth, WideHeight);
        var lastAfter = FindText(scrolled, "User Scroll 16", leftmost: true);
        Require(consumed && lastAfter.Rect.Y < lastBefore.Rect.Y && IsVisible(lastAfter),
            "Settings navigation did not scroll its final provider into view.");

        var clippedRows = scrolled.Where(command => command.Type == GpuCanvasCommandType.Text &&
                                                     command.Content.StartsWith("User Scroll ",
                                                         StringComparison.Ordinal) &&
                                                     IsNavigationCommand(command) &&
                                                     !IsVisible(command) &&
                                                     command.Rect.Y + command.Rect.Height / 2 > 0 &&
                                                     command.Rect.Y + command.Rect.Height / 2 < WideHeight)
            .ToArray();
        Require(clippedRows.Length > 0,
            "Navigation fixture did not produce a clipped row for hit-test regression coverage.");
        var clippedRow = clippedRows[0];
        Click(window, WideWidth, WideHeight, Center(clippedRow.Rect));
        Require(HasVisibleText(Render(window, WideWidth, WideHeight),
                SettingsWindowRegressionState.UserBodyMarker),
            "Clicking a navigation row outside its active clip changed the selected provider.");

        Click(window, WideWidth, WideHeight, new Vector2(40, 20));
        TypeText(window, WideWidth, WideHeight, "needle01");
        var clamped = Render(window, WideWidth, WideHeight);
        Require(HasVisibleText(clamped, "User Scroll 01"),
            "Filtering after navigation scroll did not clamp the scroll position to visible results.");
        ClearFocusedText(window, WideWidth, WideHeight);
    }

    private static void VerifyLargeFontLayout(EditorWindow window)
    {
        var normal = new EditorPreferencesDocument
        {
            Locale = "en-US",
            EditorScale = 1,
            EditorFont = "BEngine Built-in",
            EditorFontSize = 13,
            EditorTheme = "Dark"
        };
        var large = new EditorPreferencesDocument
        {
            Locale = "en-US",
            EditorScale = 1,
            EditorFont = "BEngine Built-in",
            EditorFontSize = 24,
            EditorTheme = "Dark"
        };
        EditorAppearance.Apply(large);
        try
        {
            Select(window, SettingsWindowRegressionState.UserOverviewPath);
            Click(window, WideWidth, WideHeight, new Vector2(40, 20));
            TypeText(window, WideWidth, WideHeight, "overview");
            var commands = Render(window, WideWidth, WideHeight);
            var search = FindVisibleText(commands, "overview");
            var navigation = FindVisibleText(commands, "User Overview", leftmost: true);
            var body = FindVisibleText(commands, SettingsWindowRegressionState.UserBodyMarker);
            var footer = FindVisibleText(commands, SettingsWindowRegressionState.UserFooterMarker);
            foreach (var command in new[] { search, navigation, body, footer })
                Require(command.Rect.Height >= command.FontSize + 4,
                    $"Large-font settings text '{command.Content}' is clipped by a fixed-height control.");

            VerifyNoVerticalTextOverlap(commands, navigation.ClipRect, "large-font navigation");
            VerifyNoVerticalTextOverlap(commands, body.ClipRect, "large-font provider content");
            ClearFocusedText(window, WideWidth, WideHeight);
        }
        finally
        {
            EditorAppearance.Apply(normal);
        }
    }

    private static void VerifyTwoPaneLayout(
        IReadOnlyList<GpuCanvasCommand> commands,
        int width,
        int height,
        string navigationLabel,
        string bodyMarker)
    {
        Require(commands.Count > 0, "Settings window emitted no GPU commands.");
        foreach (var command in commands)
        {
            Require(IsFinite(command.Rect) && IsFinite(command.ClipRect) &&
                    command.Rect.Width >= 0 && command.Rect.Height >= 0 &&
                    command.ClipRect.Width >= 0 && command.ClipRect.Height >= 0,
                "Settings window emitted an invalid GPU rectangle.");
            Require(command.ClipRect.X >= -0.01f && command.ClipRect.Y >= -0.01f &&
                    command.ClipRect.Right <= width + 0.01f &&
                    command.ClipRect.Bottom <= height + 0.01f,
                "Settings window emitted a clip rectangle outside the real viewport.");
        }

        var navigation = FindText(commands, navigationLabel, leftmost: true);
        var body = FindVisibleText(commands, bodyMarker);
        Require(IsVisible(navigation) && IsVisible(body),
            "Settings navigation or selected provider content is not visible.");
        Require(navigation.ClipRect.Right <= body.ClipRect.X + 0.01f,
            "Settings navigation and provider content clips overlap.");
        Require(IsContained(body.Rect, body.ClipRect),
            "Selected provider body was rendered outside its content viewport.");
    }

    private static IReadOnlyList<GpuCanvasCommand> Render(EditorWindow window, int width, int height)
    {
        var commands = new List<GpuCanvasCommand>();
        Dispatch(window, width, height, new Event(EventType.Repaint), commands);
        return commands;
    }

    private static void Dispatch(
        EditorWindow window,
        int width,
        int height,
        Event evt,
        List<GpuCanvasCommand> commands)
    {
        BeginFrame.Invoke(null, [evt, width, height, commands]);
        try { DrawWindow.Invoke(window, null); }
        finally { EndFrame.Invoke(null, null); }
    }

    private static void ClickText(
        EditorWindow window,
        int width,
        int height,
        IReadOnlyList<GpuCanvasCommand> commands,
        string text,
        bool leftmost = false) =>
        Click(window, width, height, Center(FindVisibleText(commands, text, leftmost).Rect));

    private static void Click(EditorWindow window, int width, int height, Vector2 point)
    {
        Dispatch(window, width, height,
            new Event(EventType.MouseDown) { mousePosition = point, button = 0, clickCount = 1 }, []);
        Dispatch(window, width, height,
            new Event(EventType.MouseUp) { mousePosition = point, button = 0, clickCount = 1 }, []);
    }

    private static void DoubleClick(EditorWindow window, int width, int height, Vector2 point)
    {
        Dispatch(window, width, height,
            new Event(EventType.MouseDown) { mousePosition = point, button = 0, clickCount = 2 }, []);
        Dispatch(window, width, height,
            new Event(EventType.MouseUp) { mousePosition = point, button = 0, clickCount = 2 }, []);
    }

    private static void TypeText(EditorWindow window, int width, int height, string text)
    {
        foreach (var character in text)
            Dispatch(window, width, height, new Event(EventType.KeyDown) { character = character }, []);
    }

    private static void ClearFocusedText(EditorWindow window, int width, int height)
    {
        Dispatch(window, width, height,
            new Event(EventType.KeyDown) { modifiers = EventModifiers.Control, keyCode = KeyCode.A }, []);
        Dispatch(window, width, height,
            new Event(EventType.KeyDown) { keyCode = KeyCode.Backspace }, []);
    }

    private static GpuCanvasCommand FindVisibleText(
        IReadOnlyList<GpuCanvasCommand> commands,
        string text,
        bool leftmost = false)
    {
        var matches = commands.Where(command => command.Type == GpuCanvasCommandType.Text &&
                                                command.Content == text && IsVisible(command));
        var ordered = leftmost ? matches.OrderBy(command => command.Rect.X) : matches;
        return ordered.Cast<GpuCanvasCommand?>().FirstOrDefault() ?? throw Missing(text);
    }

    private static GpuCanvasCommand FindText(
        IReadOnlyList<GpuCanvasCommand> commands,
        string text,
        bool leftmost = false)
    {
        var matches = commands.Where(command => command.Type == GpuCanvasCommandType.Text &&
                                                command.Content == text);
        var ordered = leftmost ? matches.OrderBy(command => command.Rect.X) : matches;
        return ordered.Cast<GpuCanvasCommand?>().FirstOrDefault() ?? throw Missing(text);
    }

    private static bool HasVisibleText(IReadOnlyList<GpuCanvasCommand> commands, string text) =>
        commands.Any(command => command.Type == GpuCanvasCommandType.Text && command.Content == text &&
                                IsVisible(command));

    private static bool HasSelectedRowBackground(
        IReadOnlyList<GpuCanvasCommand> commands,
        string navigationLabel)
    {
        var text = FindVisibleText(commands, navigationLabel, leftmost: true);
        var backgrounds = commands.Where(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                                    command.Rect.X <= text.Rect.X &&
                                                    command.Rect.Right >= text.Rect.Right &&
                                                    command.Rect.Y <= text.Rect.Y &&
                                                    command.Rect.Bottom >= text.Rect.Bottom &&
                                                    command.Rect.Height >= text.Rect.Height)
            .ToArray();
        if (backgrounds.Length == 0) return false;
        var finalColor = backgrounds[^1].Color;
        return finalColor == GpuCanvasColor.FromColor(EditorAppearance.palette.Active) ||
               finalColor == GpuCanvasColor.FromColor(EditorAppearance.palette.Selection);
    }

    private static void VerifyNoVerticalTextOverlap(
        IReadOnlyList<GpuCanvasCommand> commands,
        GpuCanvasRect clip,
        string region)
    {
        var rows = commands.Where(command => command.Type == GpuCanvasCommandType.Text &&
                                             command.ClipRect == clip && IsVisible(command))
            .OrderBy(command => command.Rect.Y)
            .ThenBy(command => command.Rect.X)
            .ToArray();
        for (var index = 1; index < rows.Length; index++)
        {
            if (Math.Abs(rows[index].Rect.Y - rows[index - 1].Rect.Y) < 0.01f) continue;
            Require(rows[index - 1].Rect.Bottom <= rows[index].Rect.Y + 0.01f,
                $"Adjacent text overlaps in {region}: '{rows[index - 1].Content}' and " +
                $"'{rows[index].Content}'.");
        }
    }

    private static bool IsNavigationCommand(GpuCanvasCommand command) =>
        command.Rect.X < WideWidth / 2f && command.ClipRect.Right <= WideWidth / 2f;

    private static bool IsVisible(GpuCanvasCommand command) =>
        command.Rect.Right > command.ClipRect.X && command.Rect.X < command.ClipRect.Right &&
        command.Rect.Bottom > command.ClipRect.Y && command.Rect.Y < command.ClipRect.Bottom;

    private static bool IsContained(GpuCanvasRect inner, GpuCanvasRect outer) =>
        inner.X >= outer.X - 0.01f && inner.Y >= outer.Y - 0.01f &&
        inner.Right <= outer.Right + 0.01f && inner.Bottom <= outer.Bottom + 0.01f;

    private static bool IsFinite(GpuCanvasRect rect) =>
        float.IsFinite(rect.X) && float.IsFinite(rect.Y) &&
        float.IsFinite(rect.Width) && float.IsFinite(rect.Height);

    private static Vector2 Center(GpuCanvasRect rect) =>
        new((Fix64)(rect.X + rect.Width / 2), (Fix64)(rect.Y + rect.Height / 2));

    private static InvalidOperationException Missing(string text) =>
        new($"No GPU text command rendered '{text}'.");

    private static void Open(EditorWindow window) => OpenWindow.Invoke(window, null);
    private static void Close(EditorWindow window) => CloseWindow.Invoke(window, null);
    private static void Select(EditorWindow window, string path) => SelectPath.Invoke(window, [path]);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
