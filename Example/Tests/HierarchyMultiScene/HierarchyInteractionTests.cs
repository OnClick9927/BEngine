using BEngine.Editor;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class HierarchyInteractionTests
{
    public static void Run(SceneFixture fixture)
    {
        using var harness = new EditorApplicationHarness(fixture);
        var first = harness.InitialScene;
        var unloaded = EditorSceneManager.OpenScene(
            fixture.SecondScenePath, OpenSceneMode.AdditiveWithoutLoading) ??
                       throw new InvalidOperationException("Could not create an unloaded Hierarchy Scene.");
        harness.ExpandScene(first);
        harness.ExpandScene(unloaded);
        harness.RenderHierarchy(new Event(EventType.Layout));
        var unloadedCommands = harness.RenderHierarchy(new Event(EventType.Repaint));
        Text(unloadedCommands, "Second (Not Loaded)");
        TestAssert.Require(!unloadedCommands.Any(command =>
                command.Type == GpuCanvasCommandType.Text && command.Content == "Second Root"),
            "An AdditiveWithoutLoading Scene exposed object content in Hierarchy.");
        TestAssert.Require(EditorSceneManager.CloseScene(unloaded, removeScene: true),
            "The unloaded Hierarchy Scene could not be removed before the loaded interaction test.");

        var second = EditorSceneManager.OpenScene(fixture.SecondScenePath, OpenSceneMode.Additive) ??
                     throw new InvalidOperationException("Could not open the second Scene for Hierarchy testing.");
        harness.ExpandScene(first);
        harness.ExpandScene(second);

        harness.RenderHierarchy(new Event(EventType.Layout));
        var commands = harness.RenderHierarchy(new Event(EventType.Repaint));
        var firstScene = Text(commands, "First Scene");
        var firstRoot = Text(commands, "First Root");
        var secondScene = Text(commands, "Second Scene");
        var secondRoot = Text(commands, "Second Root");
        TestAssert.Require(firstScene.Rect.Y < firstRoot.Rect.Y &&
                           firstRoot.Rect.Y < secondScene.Rect.Y &&
                           secondScene.Rect.Y < secondRoot.Rect.Y,
            "Hierarchy did not group each GameObject beneath its owning open Scene.");

        VerifySceneContextPing(harness, firstScene, fixture);
        VerifyDoubleClickFocus(harness);
        VerifyDontDestroyOnLoadGroup(harness, first);
    }

    private static void VerifySceneContextPing(
        EditorApplicationHarness harness,
        GpuCanvasCommand sceneTitle,
        SceneFixture fixture)
    {
        GenericMenuCapture.Install();
        try
        {
            var contextClick = new Event(EventType.ContextClick)
            {
                mousePosition = Center(sceneTitle.Rect),
                button = 1
            };
            harness.RenderHierarchy(contextClick);
            TestAssert.Require(contextClick.type == EventType.Used,
                "Right-clicking a Scene title did not consume the Hierarchy event.");
            TestAssert.Require(GenericMenuCapture.Paths.Contains("Select Scene Asset", StringComparer.Ordinal),
                "A Scene title context menu cannot locate its Project asset.");
            GenericMenuCapture.Invoke("Select Scene Asset");
            var expected = Path.GetRelativePath(fixture.Workspace.RootPath, fixture.FirstScenePath)
                .Replace('\\', '/');
            TestAssert.Require(string.Equals(harness.PingedAssetPath, expected,
                    StringComparison.OrdinalIgnoreCase),
                $"Select Scene Asset pinged '{harness.PingedAssetPath}' instead of '{expected}'.");
        }
        finally
        {
            GenericMenuCapture.Clear();
        }
    }

    private static void VerifyDoubleClickFocus(EditorApplicationHarness harness)
    {
        harness.SetPlaying(false);
        var commands = harness.RenderHierarchy(new Event(EventType.Repaint));
        var root = Text(commands, "First Root");
        var point = Center(root.Rect);
        harness.SetCameraPosition(new System.Numerics.Vector2(500, 500));
        TestAssert.Require(!harness.IsSceneWindowSelected(),
            "Scene view unexpectedly started selected, so the focus transition cannot be verified.");
        harness.RenderHierarchy(new Event(EventType.MouseDown)
        {
            mousePosition = point,
            button = 0,
            clickCount = 2
        });
        harness.RenderHierarchy(new Event(EventType.MouseUp)
        {
            mousePosition = point,
            button = 0,
            clickCount = 2
        });

        TestAssert.Require(harness.SelectedGameObject?.name == "First Root",
            "Double-clicking a Hierarchy GameObject did not select that object.");
        TestAssert.Require(harness.IsSceneWindowSelected(),
            "Double-clicking a Hierarchy GameObject did not switch to the Scene window.");
        TestAssert.Require(harness.CameraPosition != new System.Numerics.Vector2(500, 500),
            "Double-clicking a Hierarchy GameObject did not frame it with the editor camera.");
    }

    private static void VerifyDontDestroyOnLoadGroup(EditorApplicationHarness harness, Scene first)
    {
        var persistentRoot = first.Find("First Root") ??
                             throw new InvalidOperationException("The persistent test root disappeared.");
        BObject.DontDestroyOnLoad(persistentRoot);
        harness.SetPlaying(true);
        harness.RenderHierarchy(new Event(EventType.Layout));
        var commands = harness.RenderHierarchy(new Event(EventType.Repaint));
        var persistentHeader = Text(commands, "DontDestroyOnLoad");
        var persistentRows = commands.Where(command =>
            command.Type == GpuCanvasCommandType.Text && command.Content == "First Root").ToArray();
        TestAssert.Require(persistentRows.Length == 1 && persistentRows[0].Rect.Y > persistentHeader.Rect.Y,
            "A persistent object was not moved into exactly one DontDestroyOnLoad Hierarchy group.");
    }

    private static GpuCanvasCommand Text(IEnumerable<GpuCanvasCommand> commands, string content) =>
        commands.SingleOrDefault(command => command.Type == GpuCanvasCommandType.Text &&
                                             command.Content == content) is { Type: GpuCanvasCommandType.Text } match
            ? match
            : throw new InvalidOperationException($"Hierarchy did not draw '{content}'.");

    private static Vector2 Center(GpuCanvasRect rect) =>
        new((Fix64)(rect.X + rect.Width / 2), (Fix64)(rect.Y + rect.Height / 2));
}
