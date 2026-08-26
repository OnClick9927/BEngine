using BEngine.Editor;
using BEngine.Physics2D;

namespace BEngine.Navigation2D.Editor;

internal static class Navigation2DGizmoDrawers
{
    private const GizmoType DrawModes = GizmoType.Selected;
    private static readonly Color SelectedColor = new(
        Fix64.Parse("0.25"), Fix64.Parse("0.85"), Fix64.One, Fix64.One);
    private static readonly Color NormalColor = new(
        Fix64.Parse("0.15"), Fix64.Parse("0.65"), Fix64.Parse("0.9"), Fix64.Parse("0.7"));
    private static readonly Color LinkSelectedColor = new(
        Fix64.One, Fix64.Parse("0.78"), Fix64.Parse("0.2"), Fix64.One);
    private static readonly Color LinkNormalColor = new(
        Fix64.Parse("0.9"), Fix64.Parse("0.62"), Fix64.Parse("0.15"), Fix64.Parse("0.7"));

    [DrawGizmo(DrawModes)]
    private static void DrawSurface(NavigationSurface2D surface, GizmoType gizmoType)
    {
        if (!CanDraw(surface)) return;
        SetColor(gizmoType);
        DrawLocalBox(surface.transform, surface.center, surface.size);
    }

    [DrawGizmo(DrawModes)]
    private static void DrawObstacle(NavigationObstacle2D obstacle, GizmoType gizmoType)
    {
        if (!CanDraw(obstacle)) return;
        SetColor(gizmoType);
        var center = obstacle.transform.TransformPoint(obstacle.center);
        if (obstacle.shape == NavigationObstacleShape2D.Circle)
        {
            Gizmos.DrawWireSphere(center, obstacle.radius * MaximumAbsoluteScale(obstacle.transform));
            return;
        }
        DrawLocalBox(obstacle.transform, obstacle.center, obstacle.size);
    }

    [DrawGizmo(DrawModes)]
    private static void DrawModifierVolume(NavigationModifierVolume2D volume, GizmoType gizmoType)
    {
        if (!CanDraw(volume)) return;
        SetColor(gizmoType);
        DrawLocalBox(volume.transform, volume.center, volume.size);
    }

    [DrawGizmo(DrawModes)]
    private static void DrawModifier(NavigationModifier2D modifier, GizmoType gizmoType)
    {
        if (!CanDraw(modifier)) return;
        SetColor(gizmoType);
        if (modifier.GetComponent<Collider2D>() is { } collider)
        {
            Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size);
            return;
        }
        DrawLocalBox(modifier.transform, Vector2.zero, Vector2.one);
    }

    [DrawGizmo(DrawModes)]
    private static void DrawLink(NavigationLink2D link, GizmoType gizmoType)
    {
        if (!CanDraw(link)) return;
        Gizmos.color = gizmoType.HasFlag(GizmoType.Selected) ? LinkSelectedColor : LinkNormalColor;
        var start = link.worldStart;
        var end = link.worldEnd;
        if (link.bidirectional)
        {
            Gizmos.DrawArrow(start, end);
            Gizmos.DrawArrow(end, start);
        }
        else
        {
            Gizmos.DrawArrow(start, end);
        }

        var delta = end - start;
        if (link.width <= Fix64.Epsilon || delta.sqrMagnitude <= Fix64.Epsilon) return;
        var direction = delta.normalized;
        var perpendicular = new Vector2(-direction.y, direction.x);
        var halfWidth = link.width * MaximumAbsoluteScale(link.transform) * Fix64.Half;
        var offset = perpendicular * halfWidth;
        Gizmos.DrawLine(start + offset, end + offset);
        Gizmos.DrawLine(start - offset, end - offset);
    }

    [DrawGizmo(DrawModes)]
    private static void DrawAgent(NavigationAgent2D agent, GizmoType gizmoType)
    {
        if (!CanDraw(agent)) return;
        SetColor(gizmoType);
        Gizmos.DrawWireSphere(agent.transform.position,
            agent.radius * MaximumAbsoluteScale(agent.transform));
        if (agent.hasPath) Gizmos.DrawArrow(agent.transform.position, agent.destination);
    }

    private static bool CanDraw(Component component) =>
        component.enabled && component.gameObject.activeInHierarchy;

    private static void SetColor(GizmoType gizmoType) =>
        Gizmos.color = gizmoType.HasFlag(GizmoType.Selected) ? SelectedColor : NormalColor;

    private static void DrawLocalBox(Transform transform, Vector2 localCenter, Vector2 localSize)
    {
        var scale = transform.lossyScale;
        var worldSize = new Vector2(
            Fix64.Abs(localSize.x * scale.x),
            Fix64.Abs(localSize.y * scale.y));
        Gizmos.DrawWireCube(transform.TransformPoint(localCenter), worldSize, transform.rotation);
    }

    private static Fix64 MaximumAbsoluteScale(Transform transform) =>
        Fix64.Max(Fix64.Abs(transform.lossyScale.x), Fix64.Abs(transform.lossyScale.y));
}
