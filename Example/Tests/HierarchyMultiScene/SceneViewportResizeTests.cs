namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class SceneViewportResizeTests
{
    internal static void Run(SceneFixture fixture)
    {
        using var harness = new EditorApplicationHarness(fixture);
        harness.SetCameraPosition(new System.Numerics.Vector2(1.5f, -2.25f));
        harness.SetCameraSize(5);

        const int baselineWidth = 640;
        const int baselineHeight = 360;
        const int widerWidth = 960;
        const int tallerHeight = 640;
        var baseline = harness.ResolveEditorCamera(baselineHeight);
        var wider = harness.ResolveEditorCamera(baselineHeight);
        var taller = harness.ResolveEditorCamera(tallerHeight);
        var worldPoint = new Vector2(4, 1);

        var baselineOffset = CenterOffset(baseline, worldPoint, baselineWidth, baselineHeight);
        var widerOffset = CenterOffset(wider, worldPoint, widerWidth, baselineHeight);
        var tallerOffset = CenterOffset(taller, worldPoint, baselineWidth, tallerHeight);
        RequireNear(baselineOffset, widerOffset,
            "Changing the Scene width changed the scale or placement of existing scene content.");
        RequireNear(baselineOffset, tallerOffset,
            "Changing the Scene height changed the scale or placement of existing scene content.");

        var baselineUnit = ProjectedUnitLength(baseline, baselineWidth, baselineHeight);
        var widerUnit = ProjectedUnitLength(wider, widerWidth, baselineHeight);
        var tallerUnit = ProjectedUnitLength(taller, baselineWidth, tallerHeight);
        TestAssert.Require(MathF.Abs(baselineUnit - widerUnit) <= 0.001f &&
                           MathF.Abs(baselineUnit - tallerUnit) <= 0.001f,
            "A Scene resize changed the pixels-per-world-unit scale.");

        var baselineWorldPerPixel = harness.ResolveEditorWorldUnitsPerPixel(baselineHeight);
        var tallerWorldPerPixel = harness.ResolveEditorWorldUnitsPerPixel(tallerHeight);
        TestAssert.Require(MathF.Abs(baselineWorldPerPixel - tallerWorldPerPixel) <= 0.00001f,
            "Scene handles, panning and picking did not retain the render viewport scale after resize.");
        TestAssert.Require(wider.ViewBoundary(widerWidth, baselineHeight)[1].x >
                           baseline.ViewBoundary(baselineWidth, baselineHeight)[1].x &&
                           taller.ViewBoundary(baselineWidth, tallerHeight)[2].y >
                           baseline.ViewBoundary(baselineWidth, baselineHeight)[2].y,
            "A larger Scene viewport fitted the old composition instead of revealing more world space.");
    }

    private static System.Numerics.Vector2 CenterOffset(
        BEngine.Rendering.RenderCamera camera,
        Vector2 world,
        int width,
        int height) =>
        camera.WorldToViewport(world, width, height) -
        new System.Numerics.Vector2(width * 0.5f, height * 0.5f);

    private static float ProjectedUnitLength(
        BEngine.Rendering.RenderCamera camera,
        int width,
        int height)
    {
        var origin = camera.WorldToViewport(camera.Position, width, height);
        var unit = camera.WorldToViewport(camera.CameraToWorld(Vector2.right), width, height);
        return System.Numerics.Vector2.Distance(origin, unit);
    }

    private static void RequireNear(
        System.Numerics.Vector2 left,
        System.Numerics.Vector2 right,
        string message) =>
        TestAssert.Require(System.Numerics.Vector2.Distance(left, right) <= 0.001f,
            $"{message} Baseline={left}, resized={right}.");
}
