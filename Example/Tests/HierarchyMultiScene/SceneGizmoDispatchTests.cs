using System.Collections;
using System.Reflection;
using BEngine.Editor;
using BEngine.Rendering;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class SceneGizmoDispatchTests
{
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Assembly EditorAssembly = typeof(EditorWindow).Assembly;
    private static readonly Type PassType = TestAssert.RequireType(
        EditorAssembly, "BEngine.Editor.SceneGizmoPass");
    private static readonly Type RegistryType = TestAssert.RequireType(
        EditorAssembly, "BEngine.Editor.GizmoDrawerRegistry");
    private static readonly Type VisibilityType = TestAssert.RequireType(
        EditorAssembly, "BEngine.Editor.SceneGizmoVisibility");

    internal static void Run()
    {
        RuntimeTypeCache.Warmup();
        var wasEnabled = ReadEnabled();
        var wasVisible = IsVisible(typeof(DerivedSceneGizmoProbe));
        var scene = new Scene("Scene Gizmo dispatch");
        try
        {
            SetEnabled(true);
            SetVisible(typeof(DerivedSceneGizmoProbe), true);
            VerifyBuiltInDrawerVisibilityAndCameraAspect();
            VerifyThickLineGeometry();
            VerifySelectionDispatch(scene);
            VerifySceneObjectVisibility(scene);
            VerifyTypeVisibility(scene);
            VerifyDrawableTypeDescriptions();
        }
        finally
        {
            SetVisible(typeof(DerivedSceneGizmoProbe), wasVisible);
            SetEnabled(wasEnabled);
            scene.Dispose();
        }
    }

    private static void VerifyBuiltInDrawerVisibilityAndCameraAspect()
    {
        var previousResolution = Screen.currentResolution;
        var previousMode = Screen.fullScreenMode;
        var cameraWasVisible = IsVisible(typeof(Camera2D));
        var spriteWasVisible = IsVisible(typeof(SpriteRenderer));
        var particlesWereVisible = IsVisible(typeof(ParticleSystem2D));
        var scene = new Scene("Built-in Gizmo dispatch");
        try
        {
            SetVisible(typeof(Camera2D), true);
            SetVisible(typeof(SpriteRenderer), true);
            SetVisible(typeof(ParticleSystem2D), true);
            Screen.SetResolution(1200, 800, previousMode, previousResolution.refreshRate);
            var cameraObject = scene.CreateGameObject("Camera Gizmo");
            var camera = cameraObject.AddComponent<Camera2D>();
            camera.viewportRect = new Rect(0, 0, Fix64.Parse("0.75"), Fix64.Half);
            var spriteObject = scene.CreateGameObject("Sprite Gizmo");
            spriteObject.AddComponent<SpriteRenderer>();
            var particleObject = scene.CreateGameObject("Particle Gizmo");
            particleObject.AddComponent<ParticleSystem2D>();

            TestAssert.Require(LineCount(Collect([scene], null, 640, 360)) == 0,
                "A Camera2D Gizmo was drawn without selecting its GameObject.");

            var wideCameraLines = ReadLines(Collect([scene], cameraObject, 1200, 400));
            var tallCameraLines = ReadLines(Collect([scene], cameraObject, 400, 1200));
            TestAssert.Require(wideCameraLines.SequenceEqual(tallCameraLines),
                "The Camera2D Gizmo changed its world-space shape with the Scene viewport aspect ratio.");
            TestAssert.Require(wideCameraLines.All(line =>
                    line.Color.r == Fix64.One && line.Color.g == Fix64.One &&
                    line.Color.b == Fix64.One && line.Color.a == Fix64.One &&
                    line.LineWidth == 2),
                "A built-in Camera2D Gizmo did not use the white, thicker default line style.");
            TestAssert.Require(wideCameraLines.Length == 6,
                "A selected Camera2D did not emit its four-line boundary and two diagonals.");
            var lowerLeft = wideCameraLines[0].From;
            var lowerRight = wideCameraLines[0].To;
            var upperRight = wideCameraLines[1].To;
            var upperLeft = wideCameraLines[2].To;
            TestAssert.Require(
                Connects(wideCameraLines[4], lowerLeft, upperRight) &&
                Connects(wideCameraLines[5], lowerRight, upperLeft),
                "The selected Camera2D Gizmo did not connect opposite view-boundary corners as an X.");

            var selectedSpriteLineCount = LineCount(Collect([scene], spriteObject, 640, 360));
            TestAssert.Require(selectedSpriteLineCount == 4,
                $"The selected SpriteRenderer emitted {selectedSpriteLineCount} lines instead of its " +
                "four-line Gizmo.");
            var selectedParticleLineCount = LineCount(Collect([scene], particleObject, 640, 360));
            TestAssert.Require(selectedParticleLineCount == 3,
                $"The selected ParticleSystem2D emitted {selectedParticleLineCount} lines instead of its " +
                "three-line direction Gizmo.");
            TestAssert.Require(LineCount(Collect([scene], cameraObject, 640, 360)) == 6,
                "Selecting Camera2D did not draw its view boundary and diagonal Gizmo.");
        }
        finally
        {
            SetVisible(typeof(Camera2D), cameraWasVisible);
            SetVisible(typeof(SpriteRenderer), spriteWasVisible);
            SetVisible(typeof(ParticleSystem2D), particlesWereVisible);
            Screen.SetResolution(previousResolution.width, previousResolution.height,
                previousMode, previousResolution.refreshRate);
            scene.Dispose();
        }
    }

    private static void VerifySelectionDispatch(Scene scene)
    {
        var selectedObject = scene.CreateGameObject("Selected Gizmo Probe");
        var selected = selectedObject.AddComponent<DerivedSceneGizmoProbe>();
        selected.enabled = false;
        var ordinaryObject = scene.CreateGameObject("Ordinary Gizmo Probe");
        var ordinary = ordinaryObject.AddComponent<DerivedSceneGizmoProbe>();
        var inactiveObject = scene.CreateGameObject("Inactive Gizmo Probe");
        var inactive = inactiveObject.AddComponent<DerivedSceneGizmoProbe>();
        inactiveObject.activeSelf = false;

        SceneGizmoDrawerProbe.Reset();
        var list = Collect([scene], selectedObject, 640, 360);
        TestAssert.Require(selected.SelectedCalls == 1 && selected.DrawCalls == 0,
            "The exact selected GameObject did not exclusively receive OnDrawGizmosSelected.");
        TestAssert.Require(ordinary.DrawCalls == 1 && ordinary.SelectedCalls == 0,
            "An unselected GameObject did not exclusively receive OnDrawGizmos.");
        TestAssert.Require(inactive.DrawCalls == 1 && inactive.SelectedCalls == 0,
            "An inactive, unselected GameObject did not receive OnDrawGizmos.");
        TestAssert.Require(SceneGizmoDrawerProbe.Calls == 3 &&
                           SceneGizmoDrawerProbe.SelectedCalls == 1 &&
                           SceneGizmoDrawerProbe.NonSelectedCalls == 2,
            "A base component DrawGizmo callback was not applied to all derived components.");
        TestAssert.Require(SceneGizmoDrawerProbe.ColorWasReset,
            "Gizmos.color was not reset before a static DrawGizmo callback.");
        TestAssert.Require(SceneGizmoDrawerProbe.LineWidthWasReset,
            "Gizmos.lineWidth was not reset before a static DrawGizmo callback.");
        TestAssert.Require(LineCount(list) == 6,
            "Scene Gizmo collection returned an unexpected number of callback lines.");
    }

    private static void VerifyThickLineGeometry()
    {
        var method = typeof(PortableSceneRenderer).GetMethod("AddThickLine",
                         BindingFlags.Static | BindingFlags.NonPublic) ??
                     throw new MissingMethodException(typeof(PortableSceneRenderer).FullName,
                         "AddThickLine");
        var vertices = new List<float>();
        method.Invoke(null,
        [
            vertices,
            new System.Numerics.Vector2(10, 20),
            new System.Numerics.Vector2(30, 20),
            System.Numerics.Vector4.One,
            2f
        ]);
        TestAssert.Require(vertices.Count == 36,
            "A thick Gizmo line was not expanded into two renderable triangles.");
        var yCoordinates = Enumerable.Range(0, vertices.Count / 6)
            .Select(index => vertices[index * 6 + 1]).ToArray();
        TestAssert.Require(MathF.Abs(yCoordinates.Min() - 19) <= 0.001f &&
                           MathF.Abs(yCoordinates.Max() - 21) <= 0.001f,
            "The default Gizmo line did not occupy two screen pixels.");
        TestAssert.Require(ScreenWinding(vertices, 0) < 0 && ScreenWinding(vertices, 18) < 0,
            "A thick Gizmo line used back-facing triangle winding and would be culled.");
    }

    private static float ScreenWinding(IReadOnlyList<float> vertices, int offset)
    {
        var ax = vertices[offset];
        var ay = vertices[offset + 1];
        var bx = vertices[offset + 6];
        var by = vertices[offset + 7];
        var cx = vertices[offset + 12];
        var cy = vertices[offset + 13];
        return (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
    }

    private static bool Connects(GizmoLine line, Vector2 first, Vector2 second) =>
        line.From.Equals(first) && line.To.Equals(second) ||
        line.From.Equals(second) && line.To.Equals(first);

    private static void VerifyTypeVisibility(Scene scene)
    {
        SceneGizmoDrawerProbe.Reset();
        SetVisible(typeof(DerivedSceneGizmoProbe), false);
        var hidden = Collect([scene], scene.Find("Selected Gizmo Probe"), 640, 360);
        TestAssert.Require(SceneGizmoDrawerProbe.Calls == 0 && LineCount(hidden) == 0,
            "A disabled component type still emitted Scene Gizmos.");

        SetVisible(typeof(DerivedSceneGizmoProbe), true);
        SetEnabled(false);
        var globallyHidden = Collect([scene], scene.Find("Selected Gizmo Probe"), 640, 360);
        TestAssert.Require(LineCount(globallyHidden) == 0,
            "The global Scene Gizmos switch did not suppress collection.");
        SetEnabled(true);
    }

    private static void VerifySceneObjectVisibility(Scene scene)
    {
        var ordinaryObject = scene.Find("Ordinary Gizmo Probe") ??
                             throw new InvalidOperationException("The ordinary Gizmo probe disappeared.");
        var ordinary = ordinaryObject.GetComponent<DerivedSceneGizmoProbe>() ??
                       throw new InvalidOperationException("The ordinary Gizmo component disappeared.");
        var previousCalls = ordinary.DrawCalls;
        var visibility = SceneVisibilityManager.instance;
        visibility.Hide(ordinaryObject, includeDescendants: false);
        try
        {
            SceneGizmoDrawerProbe.Reset();
            var list = Collect([scene], scene.Find("Selected Gizmo Probe"), 640, 360);
            TestAssert.Require(ordinary.DrawCalls == previousCalls &&
                               SceneGizmoDrawerProbe.Calls == 2 && LineCount(list) == 4,
                "A Scene-hidden GameObject still emitted component or attributed Gizmos.");
        }
        finally
        {
            visibility.Show(ordinaryObject, includeDescendants: false);
        }
    }

    private static void VerifyDrawableTypeDescriptions()
    {
        var property = RegistryType.GetProperty("DrawableTypes", StaticMembers) ??
                       throw new MissingMemberException(RegistryType.FullName, "DrawableTypes");
        var entries = ((IEnumerable)(property.GetValue(null) ?? Array.Empty<object>())).Cast<object>().ToArray();
        var descriptions = entries.Select(entry =>
        {
            var type = (Type)(entry.GetType().GetProperty("ComponentType")!.GetValue(entry) ??
                              throw new InvalidDataException("A Gizmo drawable type had no component type."));
            var name = (string)(entry.GetType().GetProperty("DisplayName")!.GetValue(entry) ?? string.Empty);
            return (Type: type, Name: name);
        }).ToArray();
        TestAssert.Require(descriptions.Any(item => item.Type == typeof(DerivedSceneGizmoProbe)),
            "An external derived component with an inherited DrawGizmo callback was absent from the menu.");
        TestAssert.Require(descriptions.All(item => !string.IsNullOrWhiteSpace(item.Name)) &&
                           descriptions.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
                           descriptions.Length,
            "Scene Gizmo menu descriptions were empty or ambiguous.");
    }

    private static object Collect(
        IReadOnlyList<Scene> scenes,
        GameObject? selected,
        int width,
        int height)
    {
        var method = TestAssert.RequireMethod(PassType, "Collect", StaticMembers,
            typeof(IReadOnlyList<Scene>), typeof(GameObject), typeof(int), typeof(int));
        return method.Invoke(null, [scenes, selected, width, height]) ??
               throw new InvalidOperationException("Scene Gizmo collection returned null.");
    }

    private static int LineCount(object drawList)
    {
        var lines = drawList.GetType().GetProperty("Lines", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(drawList) as IEnumerable;
        return lines?.Cast<object>().Count() ?? 0;
    }

    private static GizmoLine[] ReadLines(object drawList)
    {
        var lines = drawList.GetType().GetProperty("Lines", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(drawList) as IEnumerable;
        return lines?.Cast<object>().Select(line =>
        {
            var type = line.GetType();
            var from = (Vector2)(type.GetProperty("From")?.GetValue(line) ??
                                 throw new InvalidDataException("A Gizmo line had no From point."));
            var to = (Vector2)(type.GetProperty("To")?.GetValue(line) ??
                               throw new InvalidDataException("A Gizmo line had no To point."));
            var color = (Color)(type.GetProperty("Color")?.GetValue(line) ??
                                throw new InvalidDataException("A Gizmo line had no Color."));
            var lineWidth = (Fix64)(type.GetProperty("LineWidth")?.GetValue(line) ??
                                    throw new InvalidDataException("A Gizmo line had no LineWidth."));
            return new GizmoLine(from, to, color, lineWidth);
        }).ToArray() ?? [];
    }

    private static bool ReadEnabled() => (bool)(VisibilityType.GetProperty("Enabled", StaticMembers)
        ?.GetValue(null) ?? true);

    private static void SetEnabled(bool value) => VisibilityType.GetProperty("Enabled", StaticMembers)
        ?.SetValue(null, value);

    private static bool IsVisible(Type type) => (bool)(VisibilityType.GetMethod("IsVisible", StaticMembers)
        ?.Invoke(null, [type]) ?? true);

    private static void SetVisible(Type type, bool visible) => VisibilityType.GetMethod("SetVisible", StaticMembers)
        ?.Invoke(null, [type, visible]);

    private readonly record struct GizmoLine(
        Vector2 From,
        Vector2 To,
        Color Color,
        Fix64 LineWidth);
}
