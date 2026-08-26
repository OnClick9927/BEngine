using BEngine.Editor;

namespace BEngine.Physics2D.Editor;

internal static class Physics2DGizmoDrawers
{
    private const int CapsuleArcSegments = 24;
    private const GizmoType DrawModes = GizmoType.Selected;
    private static readonly Color SelectedColor = new(
        Fix64.Parse("0.35"), Fix64.Parse("1.0"), Fix64.Parse("0.45"), Fix64.One);
    private static readonly Color NormalColor = new(
        Fix64.Parse("0.25"), Fix64.Parse("0.85"), Fix64.Parse("0.35"), Fix64.Parse("0.75"));

    [DrawGizmo(DrawModes)]
    private static void DrawBoxCollider(BoxCollider2D collider, GizmoType gizmoType)
    {
        if (!BeginCollider(collider, gizmoType)) return;
        var scale = AbsoluteScale(collider.transform);
        Gizmos.DrawWireCube(collider.transform.TransformPoint(collider.offset),
            Vector2.Scale(Absolute(collider.size), scale), collider.transform.rotation);
    }

    [DrawGizmo(DrawModes)]
    private static void DrawCircleCollider(CircleCollider2D collider, GizmoType gizmoType)
    {
        if (!BeginCollider(collider, gizmoType)) return;
        var scale = AbsoluteScale(collider.transform);
        Gizmos.DrawWireSphere(collider.transform.TransformPoint(collider.offset),
            Fix64.Abs(collider.radius) * Fix64.Max(scale.x, scale.y));
    }

    [DrawGizmo(DrawModes)]
    private static void DrawCapsuleCollider(CapsuleCollider2D collider, GizmoType gizmoType)
    {
        if (!BeginCollider(collider, gizmoType)) return;
        var size = Absolute(collider.size);
        var vertical = collider.direction == CapsuleDirection2D.Vertical;
        var radius = Fix64.Min(size.x, size.y) * Fix64.Half;
        var straightHalf = Fix64.Max(Fix64.Zero,
            (vertical ? size.y : size.x) * Fix64.Half - radius);
        var points = new List<Vector2>(CapsuleArcSegments * 2 + 2);
        for (var index = 0; index <= CapsuleArcSegments; index++)
        {
            var angle = Fix64.Pi * index / CapsuleArcSegments;
            points.Add(CapsulePoint(radius, straightHalf, angle, upperArc: true, vertical));
        }
        for (var index = 0; index <= CapsuleArcSegments; index++)
        {
            var angle = Fix64.Pi + Fix64.Pi * index / CapsuleArcSegments;
            points.Add(CapsulePoint(radius, straightHalf, angle, upperArc: false, vertical));
        }
        DrawLocalLoop(collider, points);
    }

    [DrawGizmo(DrawModes)]
    private static void DrawPolygonCollider(PolygonCollider2D collider, GizmoType gizmoType)
    {
        if (!BeginCollider(collider, gizmoType) || collider.points.Count < 2) return;
        DrawLocalLoop(collider, collider.points);
    }

    private static bool BeginCollider(Collider2D collider, GizmoType gizmoType)
    {
        if (!collider.enabled || !collider.gameObject.activeInHierarchy) return false;
        Gizmos.color = gizmoType.HasFlag(GizmoType.Selected) ? SelectedColor : NormalColor;
        return true;
    }

    private static Vector2 CapsulePoint(
        Fix64 radius,
        Fix64 straightHalf,
        Fix64 angle,
        bool upperArc,
        bool vertical)
    {
        var point = new Vector2(
            Fix64.Cos(angle) * radius,
            (upperArc ? straightHalf : -straightHalf) + Fix64.Sin(angle) * radius);
        return vertical ? point : new Vector2(point.y, point.x);
    }

    private static void DrawLocalLoop(Collider2D collider, IReadOnlyList<Vector2> points)
    {
        if (points.Count < 2) return;
        for (var index = 0; index < points.Count; index++)
        {
            var from = collider.transform.TransformPoint(points[index] + collider.offset);
            var to = collider.transform.TransformPoint(points[(index + 1) % points.Count] + collider.offset);
            Gizmos.DrawLine(from, to);
        }
    }

    private static Vector2 AbsoluteScale(Transform transform) => Absolute(transform.lossyScale);

    private static Vector2 Absolute(Vector2 value) =>
        new(Fix64.Abs(value.x), Fix64.Abs(value.y));
}
