using System.Reflection;
using BEngine.Editor;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class SceneGizmoToolbarTests
{
    private const int WideWidth = 640;

    internal static void Run(SceneFixture fixture)
    {
        using var harness = new EditorApplicationHarness(fixture);
        VerifyWideToolbar(harness);
        VerifyRemovedToolsAndFixedCamera(harness);
        VerifyNarrowToolbarAndExternalTypeMenu(harness);
        VerifyThresholdStability(harness);
    }

    private static void VerifyWideToolbar(EditorApplicationHarness harness)
    {
        harness.RenderScene(new Event(EventType.Layout), WideWidth);
        var commands = harness.RenderScene(new Event(EventType.Repaint), WideWidth);
        var label = commands.SingleOrDefault(command =>
            command.Type == GpuCanvasCommandType.Text && command.Content == "Gizmos");
        TestAssert.Require(label.Type == GpuCanvasCommandType.Text && label.Rect.Right <= WideWidth,
            "The wide Scene toolbar did not expose its Gizmos toggle label inside the window.");
        TestAssert.Require(!commands.Any(command => command.Type == GpuCanvasCommandType.Text &&
                                                    command.Content is "2D" or "No selection") &&
                           commands.Count(command => command.Type == GpuCanvasCommandType.Image &&
                                                     command.Content.EndsWith("View.png",
                                                         StringComparison.Ordinal)) == 1 &&
                           !commands.Any(command => command.Type == GpuCanvasCommandType.Image &&
                                                    command.Content.EndsWith("Rect.png",
                                                        StringComparison.Ordinal)),
            "The Scene toolbar did not render its distinct Q/View tool, or restored removed controls.");
        RequireGizmoIcon(commands, WideWidth);
    }

    private static void VerifyRemovedToolsAndFixedCamera(EditorApplicationHarness harness)
    {
        harness.HandleGlobalKeyboard(new Event(EventType.KeyDown) { keyCode = KeyCode.W });
        var q = new Event(EventType.KeyDown) { keyCode = KeyCode.Q };
        harness.HandleGlobalKeyboard(q);
        var t = new Event(EventType.KeyDown) { keyCode = KeyCode.T };
        harness.HandleGlobalKeyboard(t);
        TestAssert.Require(harness.CurrentTool == Tool.View &&
                           q.type == EventType.Used && t.type == EventType.KeyDown,
            "Q did not select View/Pan, or removed T still selected a Scene tool.");

        harness.RenderScene(new Event(EventType.MouseMove)
        {
            mousePosition = new Vector2(320, 240)
        }, WideWidth);
        TestAssert.Require(RequestedMouseCursor() == MouseCursor.Pan,
            "Q/View did not show the Pan hand over the Scene content.");
        harness.RenderScene(new Event(EventType.MouseMove)
        {
            mousePosition = new Vector2(320, 8)
        }, WideWidth);
        TestAssert.Require(RequestedMouseCursor() == MouseCursor.Arrow,
            "Q/View showed the Pan hand over the Scene toolbar.");
        harness.HandleGlobalKeyboard(new Event(EventType.KeyDown) { keyCode = KeyCode.W });
        harness.RenderScene(new Event(EventType.MouseMove)
        {
            mousePosition = new Vector2(320, 240)
        }, WideWidth);
        TestAssert.Require(RequestedMouseCursor() == MouseCursor.Arrow,
            "A non-View Scene tool showed the Pan hand.");
        harness.HandleGlobalKeyboard(new Event(EventType.KeyDown) { keyCode = KeyCode.Q });

        var resolveStandardCursor = typeof(EditorWindow).Assembly
            .GetType("BEngine.Editor.ImGuiNativeWindow", true)!
            .GetMethod("ResolveStandardCursor", BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new MissingMethodException("ImGuiNativeWindow", "ResolveStandardCursor");
        TestAssert.Require(string.Equals(
                resolveStandardCursor.Invoke(null, [MouseCursor.Pan])?.ToString(),
                "Hand", StringComparison.Ordinal),
            "The native editor mapped the Scene Pan cursor to a non-hand cursor.");

        harness.SetCameraSize(5);
        var beforePan = harness.CameraPosition;
        harness.RenderScene(new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(320, 240),
            button = 0
        }, WideWidth);
        TestAssert.Require(GUIUtility.hotControl != 0 && harness.SceneNavigationButton == 0 &&
                           RequestedMouseCursor() == MouseCursor.Pan,
            "Q/View did not capture its left-button pan while retaining the hand cursor.");
        var panDrag = new Event(EventType.MouseDrag)
        {
            mousePosition = new Vector2(340, 250),
            delta = new Vector2(20, 10),
            button = 0
        };
        harness.RenderScene(panDrag, WideWidth);
        TestAssert.Require(panDrag.type == EventType.Used && RequestedMouseCursor() == MouseCursor.Pan,
            $"Q/View did not consume its captured drag (hot={GUIUtility.hotControl}, " +
            $"navigationButton={harness.SceneNavigationButton}).");
        harness.RenderScene(new Event(EventType.MouseUp)
        {
            mousePosition = new Vector2(340, 250),
            button = 0
        }, WideWidth);
        TestAssert.Require(harness.CameraPosition != beforePan,
            "Dragging with the Q/View tool did not pan the Scene camera.");

        harness.RenderScene(new Event(EventType.MouseDown)
        {
            mousePosition = new Vector2(320, 240),
            button = 0
        }, WideWidth);
        TestAssert.Require(GUIUtility.hotControl != 0 && harness.SceneNavigationButton == 0,
            "Q/View did not capture its left-button pan before focus changed.");
        harness.LoseSceneFocus();
        TestAssert.Require(GUIUtility.hotControl == 0 && harness.SceneNavigationButton == -1,
            "Losing Scene focus left Q/View pan input captured.");

        var rightClick = new Event(EventType.ContextClick)
        {
            mousePosition = new Vector2(320, 240),
            button = 1
        };
        harness.RenderScene(rightClick, WideWidth);
        harness.RenderScene(new Event(EventType.MouseDrag)
        {
            mousePosition = new Vector2(340, 250),
            delta = new Vector2(20, 10),
            button = 1
        }, WideWidth);
        TestAssert.Require(rightClick.type == EventType.ContextClick &&
                           harness.ResolveEditorCamera(456).Rotation == Fix64.Zero,
            "Right mouse input was still captured to rotate the Scene camera.");
    }

    private static void VerifyNarrowToolbarAndExternalTypeMenu(EditorApplicationHarness harness)
    {
        const int narrowWidth = 120;
        harness.RenderScene(new Event(EventType.Layout), narrowWidth);
        var commands = harness.RenderScene(new Event(EventType.Repaint), narrowWidth);
        RequireGizmoIcon(commands, narrowWidth);

        GenericMenuCapture.Install();
        try
        {
            var showMenu = harness.SceneWindow.GetType().GetMethod("ShowGizmoMenu",
                BindingFlags.Static | BindingFlags.NonPublic) ??
                           throw new MissingMethodException(harness.SceneWindow.GetType().FullName,
                               "ShowGizmoMenu");
            showMenu.Invoke(null, null);
            TestAssert.Require(GenericMenuCapture.IsAdvanced,
                "The Scene Gizmos component-type selector did not open as an AdvancedDropdown.");
            TestAssert.Require(GenericMenuCapture.Paths.Contains("Gizmos", StringComparer.Ordinal) &&
                               GenericMenuCapture.Paths.Contains("All", StringComparer.Ordinal) &&
                               GenericMenuCapture.Paths.Contains("None", StringComparer.Ordinal),
                "The narrow Scene Gizmos button did not expose the global and bulk visibility controls.");
            TestAssert.Require(GenericMenuCapture.Paths.Contains(
                    "Components/Derived Scene Gizmo Probe", StringComparer.Ordinal),
                "The Scene Gizmos menu did not include an externally defined component type.");
        }
        finally
        {
            GenericMenuCapture.Clear();
        }
    }

    private static void VerifyThresholdStability(EditorApplicationHarness harness)
    {
        foreach (var width in new[] { 166, 167, 168, 169, 168, 167, 166, 190, 191, 192, 191, 190 })
        {
            harness.RenderScene(new Event(EventType.Layout), width);
            var commands = harness.RenderScene(new Event(EventType.Repaint)
            {
                mousePosition = new Vector2(width - 8, 8)
            }, width);
            RequireGizmoIcon(commands, width);
        }
    }

    private static GpuCanvasCommand RequireGizmoIcon(
        IEnumerable<GpuCanvasCommand> commands,
        int width)
    {
        var icons = commands.Where(command => command.Type == GpuCanvasCommandType.Image &&
                                              (command.Content.EndsWith("Visible.png",
                                                   StringComparison.Ordinal) ||
                                               command.Content.EndsWith("Hidden.png",
                                                   StringComparison.Ordinal)))
            .ToArray();
        TestAssert.Require(icons.Length == 1,
            $"The Scene toolbar rendered {icons.Length} Gizmos visibility icons at {width}px width.");
        var icon = icons[0];
        TestAssert.Require(icon.Rect.X >= 0 && icon.Rect.Width > 0 && icon.Rect.Right <= width + 0.01f &&
                           icon.ClipRect.X <= icon.Rect.X + 0.01f &&
                           icon.ClipRect.Right >= icon.Rect.Right - 0.01f,
            $"The Scene Gizmos button escaped its window or clip at {width}px width.");
        return icon;
    }

    private static MouseCursor RequestedMouseCursor()
    {
        var property = typeof(GUI).GetProperty("requestedMouseCursor",
            BindingFlags.Static | BindingFlags.NonPublic) ??
                       throw new MissingMemberException(typeof(GUI).FullName, "requestedMouseCursor");
        return (MouseCursor)(property.GetValue(null) ?? MouseCursor.Arrow);
    }

}
