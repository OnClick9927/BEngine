namespace BEngine;

/// <summary>Ordered, allocation free fixed point queries against a capsule chain.</summary>
public static class FixedPolylineQueries2D
{
    /// <summary>
    /// Finds the capsule contact with the earliest closest point along the query
    /// segment, then the smallest squared distance. Equal results keep the first
    /// capsule. The first point reserves the head slot; subsequent capsules join
    /// adjacent points. The explicit head is used for the head capsule.
    /// </summary>
    /// <remarks>
    /// The supplied bounds must include the head and every point. The span is
    /// read only during this call. A stationary query, or headPriority, returns
    /// a matching head before testing the remaining chain. No points means no
    /// capsules. A miss returns the head and bodyHit=false.
    /// </remarks>
    public static bool TryCapsuleHit(Vector2 from, Vector2 to, Fix64 radius,
        Vector2 head, ReadOnlySpan<Vector2> points, Vector2 minimum, Vector2 maximum,
        Fix64 headRadius, Fix64 bodyRadius, out Vector2 point, out bool bodyHit,
        bool headPriority = false)
    {
        using var kernelSample = SceneRuntimeDiagnostics.MeasureKernel(SceneRuntimeDiagnostics.KernelKind.PolylineCapsuleHit);
        point = head; bodyHit = false;
        var padding = radius + Mathf.Max(headRadius, bodyRadius);
        if (Mathf.Max(from.x, to.x) < minimum.x - padding || Mathf.Min(from.x, to.x) > maximum.x + padding ||
            Mathf.Max(from.y, to.y) < minimum.y - padding || Mathf.Min(from.y, to.y) > maximum.y + padding) return false;
        bool found = false;
        Fix64 bestTime = 2, bestDistance = 0;
        for (int i = 0; i < points.Length; i++)
        {
            var a = i == 0 ? head : points[i - 1];
            var b = i == 0 ? head : points[i];
            var reach = radius + (i == 0 ? headRadius : bodyRadius);
            if (Mathf.Max(from.x, to.x) + reach < Mathf.Min(a.x, b.x) || Mathf.Min(from.x, to.x) - reach > Mathf.Max(a.x, b.x) ||
                Mathf.Max(from.y, to.y) + reach < Mathf.Min(a.y, b.y) || Mathf.Min(from.y, to.y) - reach > Mathf.Max(a.y, b.y)) continue;
            var distance = SegmentDistanceSquared(from, to, a, b, out var contact, out var time);
            if (distance > reach * reach || (found && (time > bestTime || (time == bestTime && distance >= bestDistance)))) continue;
            found = true; bestTime = time; bestDistance = distance; point = contact; bodyHit = i > 0;
            if (i == 0 && (headPriority || from == to)) return true;
        }
        return found;
    }

    private static Fix64 Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
    private static Fix64 Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static Vector2 Closest(Vector2 p, Vector2 a, Vector2 b, out Fix64 t)
    {
        var delta = b - a;
        var squaredLength = delta.sqrMagnitude;
        t = squaredLength <= Fix64.Epsilon ? 0 : Mathf.Clamp01(Dot(p - a, delta) / squaredLength);
        return a + delta * t;
    }

    private static Fix64 SegmentDistanceSquared(Vector2 from, Vector2 to, Vector2 a, Vector2 b,
        out Vector2 contact, out Fix64 time)
    {
        var shot = to - from; var body = b - a;
        if (from == to)
        {
            contact = Closest(from, a, b, out _); time = 0;
            var pointBest = (from - contact).sqrMagnitude;
            // The identical second projection cannot change the result. Keep
            // both endpoint distances and their strict tie order, including
            // checked arithmetic failures on otherwise distant endpoints.
            var pointDistance = (from - a).sqrMagnitude;
            if (pointDistance < pointBest) { pointBest = pointDistance; contact = a; }
            pointDistance = (from - b).sqrMagnitude;
            if (pointDistance < pointBest) { pointBest = pointDistance; contact = b; }
            return pointBest;
        }
        var divisor = Cross(shot, body);
        if (Mathf.Abs(divisor) > (Fix64).00001)
        {
            var t = Cross(a - from, body) / divisor;
            var u = Cross(a - from, shot) / divisor;
            if (t >= 0 && t <= 1 && u >= 0 && u <= 1) { time = t; contact = a + body * u; return 0; }
        }
        contact = Closest(from, a, b, out _); time = 0;
        var best = (from - contact).sqrMagnitude;
        var candidate = Closest(to, a, b, out _); var distance = (to - candidate).sqrMagnitude;
        if (distance < best) { best = distance; contact = candidate; time = 1; }
        var near = Closest(a, from, to, out var along); distance = (near - a).sqrMagnitude;
        if (distance < best) { best = distance; contact = a; time = along; }
        near = Closest(b, from, to, out along); distance = (near - b).sqrMagnitude;
        if (distance < best) { best = distance; contact = b; time = along; }
        return best;
    }
}
