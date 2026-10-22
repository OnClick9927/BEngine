namespace BEngine.Physics2D;

internal static partial class CollisionAlgorithms2D
{
    private static PhysicsManifold2D ClipContactEdges(PhysicsShape2D left, PhysicsShape2D right, Vector2 normal, Fix64 penetration)
    {
        var leftEdge = FacingEdge(left, normal); var rightEdge = FacingEdge(right, -normal);
        var flip = Vector2.Dot(Outward(right, rightEdge), -normal) > Vector2.Dot(Outward(left, leftEdge), normal) + (Fix64).001;
        var reference = flip ? right : left; var incident = flip ? left : right;
        var edgeIndex = flip ? rightEdge : leftEdge;
        var faceNormal = Outward(reference, edgeIndex);
        var incidentIndex = FacingEdge(incident, -faceNormal);
        var from = reference.Vertices[edgeIndex]; var to = reference.Vertices[(edgeIndex + 1) % reference.Vertices.Length];
        var tangent = (to - from).normalized;
        Span<Vector2> points = stackalloc Vector2[2];
        Span<Vector2> clipped = stackalloc Vector2[2];
        points[0] = incident.Vertices[incidentIndex];
        points[1] = incident.Vertices[(incidentIndex + 1) % incident.Vertices.Length];
        var count = ClipSegment(points, -tangent, -Vector2.Dot(tangent, from), clipped);
        count = ClipSegment(clipped[..count], tangent, Vector2.Dot(tangent, to), points);
        Span<Vector2> contacts = stackalloc Vector2[2];
        var contactCount = 0;
        foreach (var point in points[..count])
        {
            var separation = Vector2.Dot(faceNormal, point - from);
            if (separation <= (Fix64).00001) contacts[contactCount++] = point - faceNormal * separation / 2;
        }
        if (contactCount == 0)
            return new((Support(left, normal) + Support(right, -normal)) / 2, normal, penetration);
        return new(contacts[0], normal, penetration) { Point2 = contactCount > 1 ? contacts[1] : contacts[0],
            HasSecondPoint = contactCount > 1 && (contacts[0] - contacts[1]).sqrMagnitude > (Fix64).000001 };
    }
    private static int ClipSegment(ReadOnlySpan<Vector2> points, Vector2 normal, Fix64 limit, Span<Vector2> result)
    {
        if (points.Length < 2) { points.CopyTo(result); return points.Length; }
        var a = Vector2.Dot(normal, points[0]) - limit; var b = Vector2.Dot(normal, points[1]) - limit;
        var count = 0;
        if (a <= 0) result[count++] = points[0];
        if (b <= 0) result[count++] = points[1];
        if (a < 0 && b > 0 || a > 0 && b < 0) result[count++] = points[0] + (points[1] - points[0]) * (a / (a - b));
        return count;
    }
    private static int FacingEdge(PhysicsShape2D shape, Vector2 direction)
    {
        var result = 0; var best = Fix64.FromRaw(long.MinValue);
        for (var i = 0; i < shape.Vertices.Length; i++)
        { var dot = Vector2.Dot(Outward(shape, i), direction); if (dot > best) { best = dot; result = i; } }
        return result;
    }
    private static Vector2 Outward(PhysicsShape2D shape, int index)
    {
        var edge = shape.Vertices[(index + 1) % shape.Vertices.Length] - shape.Vertices[index];
        return new Vector2(edge.y, -edge.x).normalized;
    }
}
