using BEngine.Rendering;

namespace BEngine.Editor;

internal static class CoreBuiltInGizmoDrawers
{
    private static readonly Color CameraColor = new(1, Fix64.Parse("0.72"), Fix64.Parse("0.12"));
    private static readonly Color SpriteColor = new(Fix64.Parse("0.28"), 1, Fix64.Parse("0.46"));
    private static readonly Color ParticleColor = new(Fix64.Parse("0.2"), Fix64.Parse("0.86"), 1);

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawCamera(Camera2D camera, GizmoType state)
    {
        Gizmos.color = WithSelectionAlpha(CameraColor, state);
        var viewport = camera.viewportRect;
        var width = Math.Max(1, (int)MathF.Round(
            Screen.width * (float)viewport.width));
        var height = Math.Max(1, (int)MathF.Round(
            Screen.height * (float)viewport.height));
        DrawClosed(RenderCamera.From(camera).ViewBoundary(width, height));
    }

    [DrawGizmo(GizmoType.Selected)]
    private static void DrawSprite(SpriteRenderer renderer, GizmoType state)
    {
        var visual = renderer.ResolveSpriteUnchecked();
        var pivot = renderer.useAtlasPivot && !string.IsNullOrWhiteSpace(renderer.atlas)
            ? visual.Pivot
            : renderer.pivot;
        var localCenter = new Vector2(
            (Fix64.Half - pivot.x) * renderer.size.x,
            (Fix64.Half - pivot.y) * renderer.size.y);
        var worldCenter = renderer.transform.TransformPoint(localCenter);
        var worldSize = Vector2.Scale(renderer.size, renderer.transform.lossyScale);
        Gizmos.color = WithSelectionAlpha(SpriteColor, state);
        Gizmos.DrawWireCube(worldCenter, worldSize, renderer.transform.rotation);
    }

    [DrawGizmo(GizmoType.Selected)]
    private static void DrawParticleDirection(ParticleSystem2D particles, GizmoType state)
    {
        var direction = particles.startDirection.sqrMagnitude > Fix64.Epsilon
            ? particles.startDirection.normalized
            : Vector2.up;
        var travel = direction * particles.startSpeed * Fix64.Max(Fix64.Zero, particles.startLifetime);
        var origin = particles.transform.position;
        Gizmos.color = WithSelectionAlpha(ParticleColor, state);
        Gizmos.DrawArrow(origin, origin + particles.transform.TransformVector(travel));
    }

    private static void DrawClosed(IReadOnlyList<Vector2> points)
    {
        if (points.Count < 2) return;
        for (var index = 0; index < points.Count; index++)
            Gizmos.DrawLine(points[index], points[(index + 1) % points.Count]);
    }

    private static Color WithSelectionAlpha(Color color, GizmoType state) =>
        new(color.r, color.g, color.b,
            (state & GizmoType.Selected) != 0 ? Fix64.One : Fix64.Parse("0.55"));
}
