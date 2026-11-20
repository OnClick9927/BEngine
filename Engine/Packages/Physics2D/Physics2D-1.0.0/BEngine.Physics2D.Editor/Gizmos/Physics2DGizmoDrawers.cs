using BEngine.Editor;

namespace BEngine.Physics2D.Editor;

internal static class Physics2DGizmoDrawers
{
    private const int CapsuleArcSegments = 24;
    private static readonly Vector2[] CapsuleArcPoints = CreateCapsuleArcPoints();
    private const GizmoType DrawModes = GizmoType.Selected;
    private static readonly Color SelectedColor = new(
        Fix64.FromRaw(1503238553L) /* 0.35 */, Fix64.One, Fix64.FromRaw(1932735283L) /* 0.45 */, Fix64.One);
    private static readonly Color NormalColor = new(
        Fix64.FromRaw(1073741824L) /* 0.25 */, Fix64.FromRaw(3650722201L) /* 0.85 */, Fix64.FromRaw(1503238553L) /* 0.35 */, Fix64.FromRaw(3221225472L) /* 0.75 */);

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
        var first = collider.transform.TransformPoint(
            CapsulePoint(radius, straightHalf, CapsuleArcPoints[0], upperArc: true, vertical) + collider.offset);
        var previous = first;
        for (var index = 1; index < CapsuleArcPoints.Length; index++)
        {
            var point = CapsulePoint(radius, straightHalf, CapsuleArcPoints[index],
                upperArc: index <= CapsuleArcSegments, vertical);
            var next = collider.transform.TransformPoint(point + collider.offset);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
        Gizmos.DrawLine(previous, first);
    }

    [DrawGizmo(DrawModes)]
    private static void DrawPolygonCollider(PolygonCollider2D collider, GizmoType gizmoType)
    {
        if (!BeginCollider(collider, gizmoType) || collider.points.Count < 2) return;
        DrawLocalLoop(collider, collider.points);
        foreach (var hole in collider.holes) DrawLocalLoop(collider, hole.points);
    }

    private static bool BeginCollider(Collider2D collider, GizmoType gizmoType)
    {
        if (!collider.enabled || !collider.gameObject.activeInHierarchy) return false;
        Gizmos.color = gizmoType.HasFlag(GizmoType.Selected) ? SelectedColor : NormalColor;
        return true;
    }

    [DrawGizmo(DrawModes)]
    private static void DrawCompositeCollider(CompositeCollider2D collider, GizmoType gizmoType)
    {
        if (!BeginCollider(collider, gizmoType)) return;
        foreach (var path in collider.GetGeometryPaths())
            for (var i = 0; i < path.points.Count; i++)
                Gizmos.DrawLine(collider.transform.TransformPoint(path.points[i]),
                    collider.transform.TransformPoint(path.points[(i + 1) % path.points.Count]));
    }

    [DrawGizmo(DrawModes)]
    private static void DrawEdgeCollider(EdgeCollider2D collider, GizmoType gizmoType)
    {
        if (!BeginCollider(collider, gizmoType)) return;
        for (var index = 1; index < collider.points.Count; index++)
            Gizmos.DrawLine(collider.transform.TransformPoint(collider.points[index - 1] + collider.offset),
                collider.transform.TransformPoint(collider.points[index] + collider.offset));
    }

    [DrawGizmo(DrawModes)]
    private static void DrawJoint(Joint2D joint, GizmoType gizmoType)
    {
        if (!joint.enabled || !joint.gameObject.activeInHierarchy) return;
        Gizmos.color = SelectedColor;
        var from = joint.transform.TransformPoint(joint.anchor);
        var to = joint.connectedBody?.transform.TransformPoint(joint.connectedAnchor) ?? joint.connectedAnchor;
        Gizmos.DrawLine(from, to);
        Gizmos.DrawWireSphere(from, Fix64.FromRaw(214748364L) /* 0.05 */);
        Gizmos.DrawWireSphere(to, Fix64.FromRaw(214748364L) /* 0.05 */);
    }

    private static Vector2 CapsulePoint(
        Fix64 radius,
        Fix64 straightHalf,
        Vector2 unitPoint,
        bool upperArc,
        bool vertical)
    {
        var point = new Vector2(
            unitPoint.x * radius,
            (upperArc ? straightHalf : -straightHalf) + unitPoint.y * radius);
        return vertical ? point : new Vector2(point.y, point.x);
    }

    private static Vector2[] CreateCapsuleArcPoints()
    {
        var points = new Vector2[CapsuleArcSegments * 2 + 2];
        for (var index = 0; index <= CapsuleArcSegments; index++)
        {
            var angle = Fix64.Pi * index / CapsuleArcSegments;
            points[index] = new(Fix64.Cos(angle), Fix64.Sin(angle));
            angle = Fix64.Pi + Fix64.Pi * index / CapsuleArcSegments;
            points[CapsuleArcSegments + 1 + index] = new(Fix64.Cos(angle), Fix64.Sin(angle));
        }
        return points;
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
