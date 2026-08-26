using BEngine.Editor;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class SceneHandleVisualTests
{
    private const int Width = 640;
    private const int Height = 480;
    private const float RotationRadius = 56;

    internal static void Run(SceneFixture fixture)
    {
        using var harness = new EditorApplicationHarness(fixture);
        harness.SetCameraPosition(System.Numerics.Vector2.Zero);
        harness.SetCameraSize(5);
        var selected = harness.InitialScene.Find("First Root") ??
                       throw new InvalidOperationException("The Scene handle test object was not found.");
        selected.transform.position = Vector2.zero;
        harness.SelectGameObject(selected);

        var move = RenderTool(harness, KeyCode.W, Tool.Move);
        var rotate = RenderTool(harness, KeyCode.E, Tool.Rotate);
        var scale = RenderTool(harness, KeyCode.R, Tool.Scale);

        VerifyMoveHandle(move);
        VerifyRotateHandle(rotate);
        VerifyScaleHandle(scale);
        VerifyViewportClip(move.Concat(rotate).Concat(scale));
        VerifyRotationDrag(harness, selected.transform);
        VerifyRotationOriginStability(harness, selected);
        VerifyScaleAxisAndUniformDrag(harness, selected.transform);
        VerifyFocusLossCancelsDrag(harness, selected.transform);
        VerifyToolSwitchCancelsDrag(harness, selected.transform);
    }

    private static IReadOnlyList<GpuCanvasCommand> RenderTool(
        EditorApplicationHarness harness,
        KeyCode key,
        Tool expected)
    {
        harness.HandleGlobalKeyboard(new Event(EventType.KeyDown) { keyCode = key });
        TestAssert.Require(harness.CurrentTool == expected,
            $"The {key} shortcut did not select the {expected} Scene tool.");
        harness.RenderScene(new Event(EventType.Layout), Width, Height);
        return harness.RenderScene(new Event(EventType.Repaint), Width, Height);
    }

    private static void VerifyMoveHandle(IReadOnlyList<GpuCanvasCommand> commands)
    {
        TestAssert.Require(HasAxisLabels(commands),
            "The Move handle did not expose its X/Y axes.");
        TestAssert.Require(EndpointSquares(commands).Length == 0,
            "The Move handle reused the Scale handle's square endpoint caps.");
        var pixels = HandlePixels(commands);
        TestAssert.Require(pixels.Count(pixel => pixel.Rect.Width == 2 && pixel.Rect.Height == 2) > 65,
            "The Move handle did not render distinguishable arrow shafts and heads.");
    }

    private static void VerifyRotateHandle(IReadOnlyList<GpuCanvasCommand> commands)
    {
        TestAssert.Require(!HasAxisLabels(commands),
            "The 2D Rotate handle still reused the Move/Scale X/Y axes.");
        TestAssert.Require(EndpointSquares(commands).Length == 0,
            "The Rotate handle reused square Scale endpoints.");

        var center = HandleCenter();
        var ringPixels = HandlePixels(commands).Where(command =>
        {
            var point = Center(command.Rect);
            var distance = Distance(point, center);
            return distance is >= 50 and <= 62;
        }).ToArray();
        TestAssert.Require(ringPixels.Length >= 64 &&
                           HasQuadrant(ringPixels, center, -1, -1) &&
                           HasQuadrant(ringPixels, center, 1, -1) &&
                           HasQuadrant(ringPixels, center, -1, 1) &&
                           HasQuadrant(ringPixels, center, 1, 1),
            "The Rotate handle was not rendered as a complete, immediately recognizable ring.");
    }

    private static void VerifyScaleHandle(IReadOnlyList<GpuCanvasCommand> commands)
    {
        TestAssert.Require(HasAxisLabels(commands),
            "The Scale handle did not expose its X/Y axes.");
        TestAssert.Require(EndpointSquares(commands).Length == 2,
            "The Scale handle did not render one square endpoint cap per axis.");
    }

    private static void VerifyRotationDrag(EditorApplicationHarness harness, Transform transform)
    {
        harness.HandleGlobalKeyboard(new Event(EventType.KeyDown) { keyCode = KeyCode.E });
        TestAssert.Require(harness.CurrentTool == Tool.Rotate,
            "E did not switch back to the Rotate handle before interaction testing.");

        transform.localRotation = 0;
        DragRotation(harness, 0, 90);
        TestAssert.Require(Fix64.Abs(transform.localRotation - 90) <= Fix64.Parse("0.5"),
            $"Dragging the Rotate ring by a quarter turn produced {transform.localRotation} degrees.");

        transform.localRotation = 0;
        DragRotation(harness, 170, -170);
        TestAssert.Require(Fix64.Abs(transform.localRotation - 20) <= Fix64.Parse("0.5"),
            "Dragging the Rotate ring across +/-180 degrees jumped or reversed direction.");
    }

    private static void VerifyRotationOriginStability(EditorApplicationHarness harness, GameObject originalSelection)
    {
        var offsetPivotObject = harness.InitialScene.CreateGameObject("Offset Pivot Handle Test");
        var renderer = offsetPivotObject.AddComponent<SpriteRenderer>();
        renderer.useSpritePivot = false;
        renderer.pivot = new Vector2(0, Fix64.Half);
        renderer.size = new Vector2(2, 1);
        var transform = offsetPivotObject.transform;
        harness.SelectGameObject(offsetPivotObject);
        harness.HandleGlobalKeyboard(new Event(EventType.KeyDown) { keyCode = KeyCode.E });
        var worldCenter = SceneHandleUtility.GetHandlePosition(offsetPivotObject);
        var viewportHeight = Height - (float)EditorStyles.toolbar.fixedHeight;
        var origin = HandleCenter() + new Vector2((Fix64)(viewportHeight / 10), 0);
        var down = new Event(EventType.MouseDown)
        {
            mousePosition = RingPoint(origin, 0),
            button = 0,
            clickCount = 1
        };
        harness.RenderScene(down, Width, Height);
        TestAssert.Require(down.type == EventType.Used,
            "The Rotate ring was not centered on an offset-pivot Sprite.");

        harness.RenderScene(new Event(EventType.MouseDrag)
        {
            mousePosition = RingPoint(origin, 45),
            button = 0
        }, Width, Height);
        TestAssert.Require(Vector2.Distance(SceneHandleUtility.GetHandlePosition(offsetPivotObject), worldCenter) <=
                           Fix64.Parse("0.01"),
            "The visible Rotate ring moved away from its active rotation center during a multi-frame drag.");
        harness.RenderScene(new Event(EventType.MouseDrag)
        {
            mousePosition = RingPoint(origin, 90),
            button = 0
        }, Width, Height);
        harness.RenderScene(new Event(EventType.MouseUp)
        {
            mousePosition = RingPoint(origin, 90),
            button = 0
        }, Width, Height);

        TestAssert.Require(Fix64.Abs(transform.localRotation - 90) <= Fix64.Parse("0.5") &&
                           Vector2.Distance(SceneHandleUtility.GetHandlePosition(offsetPivotObject), worldCenter) <=
                           Fix64.Parse("0.01") &&
                           Vector2.Distance(transform.position, new Vector2(1, -1)) <= Fix64.Parse("0.01"),
            "Rotating an offset-pivot Sprite did not keep the visible Handle at the object's center.");
        harness.SelectGameObject(originalSelection);
    }

    private static void VerifyScaleAxisAndUniformDrag(EditorApplicationHarness harness, Transform transform)
    {
        transform.localRotation = 90;
        Tools.pivotRotation = PivotRotation.Global;
        var commands = RenderTool(harness, KeyCode.R, Tool.Scale);
        var center = HandleCenter();
        var endpoints = EndpointSquares(commands).Select(command => Center(command.Rect)).ToArray();
        TestAssert.Require(endpoints.Any(point => Fix64.Abs(point.x - center.x) <= 1 && point.y < center.y - 60) &&
                           endpoints.Any(point => point.x < center.x - 60 && Fix64.Abs(point.y - center.y) <= 1),
            "The Scale handle axes did not follow the localScale axes of a rotated object.");

        transform.localRotation = 0;
        transform.localScale = new Vector2(2, 3);
        RenderTool(harness, KeyCode.R, Tool.Scale);
        var down = new Event(EventType.MouseDown)
        {
            mousePosition = center,
            button = 0,
            clickCount = 1
        };
        harness.RenderScene(down, Width, Height);
        TestAssert.Require(down.type == EventType.Used,
            "The Scale handle's visible center control did not capture the pointer.");
        var destination = center + new Vector2(16, -16);
        harness.RenderScene(new Event(EventType.MouseDrag)
        {
            mousePosition = destination,
            button = 0
        }, Width, Height);
        harness.RenderScene(new Event(EventType.MouseUp)
        {
            mousePosition = destination,
            button = 0
        }, Width, Height);

        TestAssert.Require(Fix64.Abs(transform.localScale.x - Fix64.Parse("2.4")) <= Fix64.Parse("0.01") &&
                           Fix64.Abs(transform.localScale.y - Fix64.Parse("3.6")) <= Fix64.Parse("0.01"),
            "Dragging the Scale center control did not preserve the object's scale proportions.");
    }

    private static void VerifyFocusLossCancelsDrag(EditorApplicationHarness harness, Transform transform)
    {
        transform.position = Vector2.zero;
        transform.localRotation = 0;
        harness.HandleGlobalKeyboard(new Event(EventType.KeyDown) { keyCode = KeyCode.E });
        var origin = HandleCenter();
        var down = new Event(EventType.MouseDown)
        {
            mousePosition = RingPoint(origin, 0),
            button = 0,
            clickCount = 1
        };
        harness.RenderScene(down, Width, Height);
        TestAssert.Require(down.type == EventType.Used,
            "The Rotate ring did not own the control before the focus-loss test.");
        harness.FocusHierarchy();
        harness.RenderScene(new Event(EventType.MouseDrag)
        {
            mousePosition = RingPoint(origin, 90),
            button = 0
        }, Width, Height);

        TestAssert.Require(transform.localRotation == 0,
            "The Scene handle kept dragging after the Scene window lost focus.");
    }

    private static void DragRotation(EditorApplicationHarness harness, float fromDegrees, float toDegrees)
    {
        var origin = HandleCenter();
        var from = RingPoint(origin, fromDegrees);
        var to = RingPoint(origin, toDegrees);
        var down = new Event(EventType.MouseDown)
        {
            mousePosition = from,
            button = 0,
            clickCount = 1
        };
        harness.RenderScene(down, Width, Height);
        TestAssert.Require(down.type == EventType.Used,
            "The Rotate ring did not capture a mouse press on its visible circumference.");
        harness.RenderScene(new Event(EventType.MouseDrag)
        {
            mousePosition = to,
            button = 0
        }, Width, Height);
        harness.RenderScene(new Event(EventType.MouseUp)
        {
            mousePosition = to,
            button = 0
        }, Width, Height);
    }

    private static void VerifyToolSwitchCancelsDrag(EditorApplicationHarness harness, Transform transform)
    {
        transform.localRotation = 0;
        harness.HandleGlobalKeyboard(new Event(EventType.KeyDown) { keyCode = KeyCode.E });
        var origin = HandleCenter();
        harness.RenderScene(new Event(EventType.MouseDown)
        {
            mousePosition = RingPoint(origin, 0),
            button = 0,
            clickCount = 1
        }, Width, Height);

        harness.HandleGlobalKeyboard(new Event(EventType.KeyDown) { keyCode = KeyCode.R });
        harness.RenderScene(new Event(EventType.MouseDrag)
        {
            mousePosition = RingPoint(origin, 90),
            button = 0
        }, Width, Height);
        harness.RenderScene(new Event(EventType.MouseUp)
        {
            mousePosition = RingPoint(origin, 90),
            button = 0
        }, Width, Height);

        TestAssert.Require(harness.CurrentTool == Tool.Scale && transform.localRotation == 0,
            "Switching W/E/R during a drag did not cancel the previous handle interaction.");
    }

    private static void VerifyViewportClip(IEnumerable<GpuCanvasCommand> commands)
    {
        var toolbarHeight = (float)EditorStyles.toolbar.fixedHeight;
        TestAssert.Require(HandlePixels(commands).All(command =>
                command.ClipRect.Y >= toolbarHeight - 0.01f &&
                command.ClipRect.Height <= Height - toolbarHeight + 0.01f),
            "Scene handles were not clipped below the Scene toolbar.");
    }

    private static GpuCanvasCommand[] HandlePixels(IEnumerable<GpuCanvasCommand> commands) => commands
        .Where(command => command.Type == GpuCanvasCommandType.SolidRect &&
                          command.Rect.Y > (float)EditorStyles.toolbar.fixedHeight + 4 &&
                          command.Rect.Width <= 12 && command.Rect.Height <= 12)
        .ToArray();

    private static GpuCanvasCommand[] EndpointSquares(IEnumerable<GpuCanvasCommand> commands) =>
        HandlePixels(commands).Where(command =>
            MathF.Abs(command.Rect.Width - 10) < 0.01f &&
            MathF.Abs(command.Rect.Height - 10) < 0.01f).ToArray();

    private static bool HasAxisLabels(IEnumerable<GpuCanvasCommand> commands)
    {
        var labels = commands.Where(command => command.Type == GpuCanvasCommandType.Text)
            .Select(command => command.Content).ToHashSet(StringComparer.Ordinal);
        return labels.Contains("X") && labels.Contains("Y");
    }

    private static bool HasQuadrant(IEnumerable<GpuCanvasCommand> commands, Vector2 center, int x, int y) =>
        commands.Any(command =>
        {
            var point = Center(command.Rect);
            return Math.Sign((float)(point.x - center.x)) == x &&
                   Math.Sign((float)(point.y - center.y)) == y;
        });

    private static Vector2 HandleCenter()
    {
        var toolbarHeight = EditorStyles.toolbar.fixedHeight;
        return new Vector2(Width / 2, toolbarHeight + (Height - toolbarHeight) / 2);
    }

    private static Vector2 RingPoint(Vector2 center, float degrees)
    {
        var radians = degrees * MathF.PI / 180;
        return center + new Vector2(
            (Fix64)(MathF.Cos(radians) * RotationRadius),
            (Fix64)(-MathF.Sin(radians) * RotationRadius));
    }

    private static Vector2 Center(GpuCanvasRect rect) => new(
        (Fix64)(rect.X + rect.Width / 2),
        (Fix64)(rect.Y + rect.Height / 2));

    private static float Distance(Vector2 left, Vector2 right)
    {
        var x = (float)(left.x - right.x);
        var y = (float)(left.y - right.y);
        return MathF.Sqrt(x * x + y * y);
    }
}
