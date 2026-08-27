using BEngine.Editor;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class HierarchyUnityStyleTests
{
    private const int WideWidth = 520;

    public static void Run(SceneFixture fixture)
    {
        using var harness = new EditorApplicationHarness(fixture);
        var first = harness.InitialScene;
        var second = EditorSceneManager.OpenScene(fixture.SecondScenePath, OpenSceneMode.Additive) ??
                     throw new InvalidOperationException("Could not open the second Scene for Hierarchy styling.");
        harness.ExpandScene(first);
        harness.ExpandScene(second);
        harness.RenderHierarchy(new Event(EventType.Layout), WideWidth);

        VerifyCompactToolbarAndSceneActions(harness);
        VerifyTreeHierarchyAndFoldouts(harness);
        VerifySearch(harness);
        VerifySelectionAndHover(harness);
        VerifyNarrowWidthTextStability(harness);
        VerifyDragAndRenameRemainInteractive(harness, first);
    }

    private static void VerifyCompactToolbarAndSceneActions(EditorApplicationHarness harness)
    {
        var commands = harness.RenderHierarchy(new Event(EventType.Repaint), WideWidth);
        var firstScene = Text(commands, "First Scene");
        var add = ToolbarImage(commands, "Add.png", firstScene);
        var dropdown = ToolbarImage(commands, "FoldoutOpen.png", firstScene);
        var search = ToolbarImage(commands, "Search.png", firstScene);

        TestAssert.Require(add.Rect.X < dropdown.Rect.X && dropdown.Rect.X < search.Rect.X,
            "Hierarchy toolbar does not expose the compact Add dropdown before Search.");
        TestAssert.Require(!commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                    command.Rect.Bottom <= firstScene.Rect.Y &&
                                                    command.Content.Contains("Create GameObject",
                                                        StringComparison.OrdinalIgnoreCase)),
            "Hierarchy rendered a verbose create label instead of Unity-style compact toolbar controls.");
        var toolbarColor = GpuCanvasColor.FromColor(EditorStyles.toolbar.normal.backgroundColor);
        TestAssert.Require(commands.Any(command => command.Type == GpuCanvasCommandType.SolidRect &&
                                                   command.Color == toolbarColor &&
                                                   command.Rect.X <= 0.5f &&
                                                   command.Rect.Width >= WideWidth - 1 &&
                                                   command.Rect.Bottom <= firstScene.Rect.Y + 1),
            "Hierarchy toolbar has no full-width Unity-style background.");

        GenericMenuCapture.Install();
        try
        {
            Click(harness, dropdown.Rect);
            TestAssert.Require(GenericMenuCapture.Paths.Contains("Create Empty", StringComparer.Ordinal),
                "Hierarchy Add dropdown does not offer Create Empty.");
        }
        finally
        {
            GenericMenuCapture.Clear();
        }

        commands = harness.RenderHierarchy(new Event(EventType.Repaint), WideWidth);
        firstScene = Text(commands, "First Scene");
        var more = RowImage(commands, "More.png", firstScene);
        TestAssert.Require(more.Rect.X > firstScene.Rect.X && more.Rect.Right >= WideWidth - 36,
            "Hierarchy Scene actions are not exposed as a right-aligned three-dot button.");

        GenericMenuCapture.Install();
        try
        {
            Click(harness, more.Rect);
            TestAssert.Require(GenericMenuCapture.Paths.Contains("Select Scene Asset", StringComparer.Ordinal),
                "Hierarchy Scene three-dot menu is missing Select Scene Asset.");
        }
        finally
        {
            GenericMenuCapture.Clear();
        }
    }

    private static void VerifyTreeHierarchyAndFoldouts(EditorApplicationHarness harness)
    {
        var collapsed = harness.RenderHierarchy(new Event(EventType.Repaint), WideWidth);
        var firstScene = Text(collapsed, "First Scene");
        var firstRoot = Text(collapsed, "First Root");
        TestAssert.Require(!HasText(collapsed, "First Child"),
            "A collapsed Hierarchy GameObject exposed its children.");

        var sceneIcon = RowImage(collapsed, "AssetScene.png", firstScene);
        var rootIcon = RowImage(collapsed, "GameObject.png", firstRoot);
        TestAssert.Require(!HasRowImage(collapsed, "More.png", firstRoot),
            "A Hierarchy GameObject row still rendered a right-side three-dot action button.");
        var rootFoldout = RowImageBefore(collapsed, "FoldoutClosed.png", firstRoot, rootIcon.Rect.X);
        TestAssert.Require(sceneIcon.Rect.X < firstScene.Rect.X && rootIcon.Rect.X < firstRoot.Rect.X,
            "Hierarchy Scene or GameObject row is missing its identifying icon.");
        var titleBar = GpuCanvasColor.FromColor(EditorStyles.windowTitle.normal.backgroundColor);
        var raisedPanel = GpuCanvasColor.FromColor(GUI.skin.frameBox.normal.backgroundColor);
        TestAssert.Require(HasFullRowBackground(collapsed, firstScene, titleBar, WideWidth) ||
                           HasFullRowBackground(collapsed, firstScene, raisedPanel, WideWidth),
            "Hierarchy Scene root has no distinct Unity-style header background.");
        TestAssert.Require(!HasFullRowBackground(collapsed, firstScene,
                GpuCanvasColor.FromColor(EditorStyles.treeViewRowSelected.normal.backgroundColor), WideWidth),
            "The active Scene was rendered as a selected GameObject instead of a Scene header.");

        Click(harness, rootFoldout.Rect);
        var expanded = harness.RenderHierarchy(new Event(EventType.Repaint), WideWidth);
        firstScene = Text(expanded, "First Scene");
        firstRoot = Text(expanded, "First Root");
        var firstChild = Text(expanded, "First Child");
        var childIcon = RowImage(expanded, "GameObject.png", firstChild);
        TestAssert.Require(firstScene.Rect.Y < firstRoot.Rect.Y && firstRoot.Rect.Y < firstChild.Rect.Y,
            "Hierarchy rows are not ordered Scene, root GameObject, then child GameObject.");
        TestAssert.Require(firstRoot.Rect.X >= firstScene.Rect.X + 8 &&
                           firstChild.Rect.X >= firstRoot.Rect.X + 8 &&
                           childIcon.Rect.X < firstChild.Rect.X,
            "Hierarchy nesting is not represented by stable per-depth indentation.");
        TestAssert.Require(RowsDoNotOverlap(firstScene, firstRoot) && RowsDoNotOverlap(firstRoot, firstChild) &&
                           Math.Abs(firstScene.Rect.Height - firstRoot.Rect.Height) <= 2 &&
                           Math.Abs(firstRoot.Rect.Height - firstChild.Rect.Height) <= 2,
            "Hierarchy Scene and GameObject rows do not share a compact, non-overlapping row height.");
        TestAssert.Require(TextFitsClip(firstScene) && TextFitsClip(firstRoot) && TextFitsClip(firstChild),
            "Hierarchy row text exceeds its visible clip.");

        var openFoldout = RowImageBefore(expanded, "FoldoutOpen.png", firstRoot,
            RowImage(expanded, "GameObject.png", firstRoot).Rect.X);
        Click(harness, openFoldout.Rect);
        var collapsedAgain = harness.RenderHierarchy(new Event(EventType.Repaint), WideWidth);
        TestAssert.Require(!HasText(collapsedAgain, "First Child"),
            "Hierarchy foldout could not collapse a GameObject after expansion.");
        var closedAgain = RowImageBefore(collapsedAgain, "FoldoutClosed.png", Text(collapsedAgain, "First Root"),
            RowImage(collapsedAgain, "GameObject.png", Text(collapsedAgain, "First Root")).Rect.X);
        Click(harness, closedAgain.Rect);
    }

    private static void VerifySearch(EditorApplicationHarness harness)
    {
        harness.SetHierarchySearch("First Child");
        harness.RenderHierarchy(new Event(EventType.Layout), WideWidth);
        var filtered = harness.RenderHierarchy(new Event(EventType.Repaint), WideWidth);
        TestAssert.Require(HasText(filtered, "First Scene") && HasText(filtered, "First Root") &&
                           HasText(filtered, "First Child"),
            "Hierarchy search did not retain a matching child and its ownership chain.");
        TestAssert.Require(!HasText(filtered, "Second Scene") && !HasText(filtered, "Second Root"),
            "Hierarchy search retained an unrelated Scene branch.");
        TestAssert.Require(filtered.Count(command => command.Type == GpuCanvasCommandType.Text &&
                                                     command.Content == "First Child") >= 2,
            "Hierarchy toolbar search field did not render the active query.");
        harness.SetHierarchySearch(string.Empty);
        harness.RenderHierarchy(new Event(EventType.Layout), WideWidth);
    }

    private static void VerifySelectionAndHover(EditorApplicationHarness harness)
    {
        harness.FocusHierarchy();
        var commands = harness.RenderHierarchy(new Event(EventType.Repaint), WideWidth);
        var root = Text(commands, "First Root");
        Click(harness, root.Rect);
        TestAssert.Require(harness.SelectedGameObject?.name == "First Root",
            "Clicking a Hierarchy row did not select its GameObject.");

        var selected = harness.RenderHierarchy(new Event(EventType.Repaint), WideWidth);
        root = Text(selected, "First Root");
        var selectionColor = GpuCanvasColor.FromColor(EditorStyles.treeViewRowSelected.normal.backgroundColor);
        TestAssert.Require(HasFullRowBackground(selected, root, selectionColor, WideWidth),
            "Hierarchy selection does not span the full Unity-style row.");

        var child = Text(selected, "First Child");
        var hovered = harness.RenderHierarchy(new Event(EventType.Repaint)
        {
            mousePosition = Center(child.Rect)
        }, WideWidth);
        child = Text(hovered, "First Child");
        var hoverColor = GpuCanvasColor.FromColor(EditorStyles.treeViewRow.hover.backgroundColor);
        TestAssert.Require(HasFullRowBackground(hovered, child, hoverColor, WideWidth),
            "Hierarchy hover does not span the full Unity-style row.");
    }

    private static void VerifyNarrowWidthTextStability(EditorApplicationHarness harness)
    {
        foreach (var width in new[] { 88, 89, 90, 89, 88 })
        {
            harness.RenderHierarchy(new Event(EventType.Layout), width);
            var commands = harness.RenderHierarchy(new Event(EventType.Repaint), width);
            foreach (var label in new[] { "First Scene", "First Root", "First Child" })
            {
                var text = Text(commands, label);
                TestAssert.Require(text.Rect.Width > 0 && text.ClipRect.Width > 0 && TextFitsClip(text),
                    $"Hierarchy text '{label}' disappeared or escaped its clip at {width}px width.");
            }
        }
        harness.RenderHierarchy(new Event(EventType.Layout), WideWidth);
    }

    private static void VerifyDragAndRenameRemainInteractive(EditorApplicationHarness harness, Scene scene)
    {
        var child = scene.Find("First Child") ??
                    throw new InvalidOperationException("Hierarchy drag test child was not found.");
        var commands = harness.RenderHierarchy(new Event(EventType.Repaint), WideWidth);
        var childRow = Text(commands, "First Child");
        var sceneRow = Text(commands, "First Scene");
        var childPoint = Center(childRow.Rect);
        var scenePoint = Center(sceneRow.Rect);
        harness.RenderHierarchy(new Event(EventType.MouseDown)
        {
            mousePosition = childPoint, button = 0, clickCount = 1
        }, WideWidth);
        harness.RenderHierarchy(new Event(EventType.MouseDrag)
        {
            mousePosition = childPoint + new Vector2(8, 0), button = 0
        }, WideWidth);
        harness.RenderHierarchy(new Event(EventType.MouseDrag)
        {
            mousePosition = scenePoint, button = 0
        }, WideWidth);
        var drop = new Event(EventType.MouseUp) { mousePosition = scenePoint, button = 0 };
        harness.RenderHierarchy(drop, WideWidth);
        TestAssert.Require(drop.type == EventType.Used && child.transform.parent is null &&
                           ReferenceEquals(harness.SelectedGameObject, child),
            "Hierarchy drag-and-drop no longer reparents a child to its Scene root.");

        var root = scene.Find("First Root") ??
                   throw new InvalidOperationException("Hierarchy rename test root was not found.");
        commands = harness.RenderHierarchy(new Event(EventType.Repaint), WideWidth);
        Click(harness, Text(commands, "First Root").Rect);
        var beginRename = new Event(EventType.KeyDown) { keyCode = KeyCode.F2 };
        harness.RenderHierarchy(beginRename, WideWidth);
        TestAssert.Require(beginRename.type == EventType.Used && harness.HierarchyRenamingId == root.Id,
            "Hierarchy F2 rename stopped working after the Unity-style row changes.");
        harness.SetHierarchyRenameValue("Renamed Hierarchy Root");
        var requestedRename = harness.HierarchyRenameValue;
        var focusBeforeCommit = harness.GuiFocusState;
        var commitRename = new Event(EventType.KeyDown) { keyCode = KeyCode.Return };
        harness.RenderHierarchy(commitRename, WideWidth);
        TestAssert.Require(root.name == "Renamed Hierarchy Root",
            $"Hierarchy rename could not commit after the Unity-style row changes " +
            $"(name: '{root.name}', requested: '{requestedRename}', pending: " +
            $"'{harness.HierarchyRenameValue}', focus-before: {focusBeforeCommit}, " +
            $"event: {commitRename.type}, control: {GUIUtility.keyboardControl}).");
    }

    private static bool HasFullRowBackground(IEnumerable<GpuCanvasCommand> commands,
        GpuCanvasCommand row, GpuCanvasColor color, int width) => commands.Any(command =>
        command.Type == GpuCanvasCommandType.SolidRect && command.Color == color &&
        command.Rect.Y <= row.Rect.Y + 0.5f && command.Rect.Bottom >= row.Rect.Bottom - 0.5f &&
        command.Rect.X <= 1.5f && command.Rect.Width >= width - 20);

    private static bool RowsDoNotOverlap(GpuCanvasCommand upper, GpuCanvasCommand lower) =>
        upper.Rect.Bottom <= lower.Rect.Y + 0.5f;

    private static bool TextFitsClip(GpuCanvasCommand command) =>
        command.Rect.X >= command.ClipRect.X - 0.01f &&
        command.Rect.Right <= command.ClipRect.Right + 0.01f &&
        command.Rect.Y >= command.ClipRect.Y - 0.01f &&
        command.Rect.Bottom <= command.ClipRect.Bottom + 0.01f;

    private static void Click(EditorApplicationHarness harness, GpuCanvasRect rect)
    {
        var point = Center(rect);
        harness.RenderHierarchy(new Event(EventType.MouseDown)
        {
            mousePosition = point, button = 0, clickCount = 1
        }, WideWidth);
        harness.RenderHierarchy(new Event(EventType.MouseUp)
        {
            mousePosition = point, button = 0, clickCount = 1
        }, WideWidth);
    }

    private static GpuCanvasCommand ToolbarImage(IEnumerable<GpuCanvasCommand> commands, string suffix,
        GpuCanvasCommand firstRow) => commands.SingleOrDefault(command =>
        command.Type == GpuCanvasCommandType.Image && command.Content.EndsWith(suffix, StringComparison.Ordinal) &&
        command.Rect.Bottom <= firstRow.Rect.Y + 0.5f) is { Type: GpuCanvasCommandType.Image } match
            ? match
            : throw new InvalidOperationException($"Hierarchy toolbar did not draw '{suffix}'.");

    private static GpuCanvasCommand RowImage(IEnumerable<GpuCanvasCommand> commands, string suffix,
        GpuCanvasCommand row) => commands.SingleOrDefault(command =>
        command.Type == GpuCanvasCommandType.Image && command.Content.EndsWith(suffix, StringComparison.Ordinal) &&
        VerticallyOverlaps(command.Rect, row.Rect)) is { Type: GpuCanvasCommandType.Image } match
            ? match
            : throw new InvalidOperationException($"Hierarchy row '{row.Content}' did not draw '{suffix}'.");

    private static bool HasRowImage(IEnumerable<GpuCanvasCommand> commands, string suffix,
        GpuCanvasCommand row) => commands.Any(command =>
        command.Type == GpuCanvasCommandType.Image &&
        command.Content.EndsWith(suffix, StringComparison.Ordinal) &&
        command.Rect.Y < row.Rect.Bottom && command.Rect.Bottom > row.Rect.Y);

    private static GpuCanvasCommand RowImageBefore(IEnumerable<GpuCanvasCommand> commands, string suffix,
        GpuCanvasCommand row, float beforeX) => commands.SingleOrDefault(command =>
        command.Type == GpuCanvasCommandType.Image && command.Content.EndsWith(suffix, StringComparison.Ordinal) &&
        command.Rect.X < beforeX && VerticallyOverlaps(command.Rect, row.Rect)) is
            { Type: GpuCanvasCommandType.Image } match
            ? match
            : throw new InvalidOperationException($"Hierarchy row '{row.Content}' did not draw '{suffix}'.");

    private static bool VerticallyOverlaps(GpuCanvasRect left, GpuCanvasRect right) =>
        left.Y < right.Bottom && left.Bottom > right.Y;

    private static bool HasText(IEnumerable<GpuCanvasCommand> commands, string content) => commands.Any(command =>
        command.Type == GpuCanvasCommandType.Text && command.Content == content);

    private static GpuCanvasCommand Text(IEnumerable<GpuCanvasCommand> commands, string content) =>
        commands.SingleOrDefault(command => command.Type == GpuCanvasCommandType.Text &&
                                             command.Content == content) is
            { Type: GpuCanvasCommandType.Text } match
            ? match
            : throw new InvalidOperationException($"Hierarchy did not draw '{content}'.");

    private static Vector2 Center(GpuCanvasRect rect) =>
        new((Fix64)(rect.X + rect.Width / 2), (Fix64)(rect.Y + rect.Height / 2));
}
