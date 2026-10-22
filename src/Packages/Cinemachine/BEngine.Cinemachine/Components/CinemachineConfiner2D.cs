namespace BEngine.Cinemachine;

[AddComponentMenu("Cinemachine/Confiner 2D")]
public sealed class CinemachineConfiner2D : Behaviour
{
    public List<Vector2> path { get; set; } =
    [
        new(-5, -5), new(5, -5), new(5, 5), new(-5, 5)
    ];

    public Vector2 ConstrainPoint(Vector2 worldPoint)
    {
        if (path.Count < 3) return worldPoint;
        var localPoint = transform.InverseTransformPoint(worldPoint);
        if (Contains(localPoint)) return worldPoint;
        var closest = path[0];
        var bestDistance = Fix64.FromRaw(long.MaxValue);
        for (var index = 0; index < path.Count; index++)
        {
            var candidate = ClosestPointOnSegment(
                localPoint, path[index], path[(index + 1) % path.Count]);
            var distance = (candidate - localPoint).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            closest = candidate;
        }
        return transform.TransformPoint(closest);
    }

    private bool Contains(Vector2 point)
    {
        var inside = false;
        for (int current = 0, previous = path.Count - 1; current < path.Count; previous = current++)
        {
            var a = path[current];
            var b = path[previous];
            if ((a.y > point.y) == (b.y > point.y)) continue;
            var denominator = b.y - a.y;
            if (Fix64.Abs(denominator) <= Fix64.Epsilon) continue;
            var x = (b.x - a.x) * (point.y - a.y) / denominator + a.x;
            if (point.x < x) inside = !inside;
        }
        return inside;
    }

    private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 from, Vector2 to)
    {
        var segment = to - from;
        var lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= Fix64.Epsilon) return from;
        return from + segment * Mathf.Clamp01(Vector2.Dot(point - from, segment) / lengthSquared);
    }
}
