namespace BEngine;

/// <summary>Explicit fixed point edges, without normalization or float conversion.</summary>
public readonly record struct FixedRectangleEdges2D(Fix64 Left, Fix64 Right, Fix64 Bottom, Fix64 Top);

/// <summary>Ordered, allocation free queries against immutable rectangle snapshots.</summary>
public static class FixedRectangleQueries2D
{
    /// <summary>Tests a padded segment against centered bounds and an ordered rectangle snapshot.</summary>
    public static bool IsSegmentClearWithinBounds(Vector2 from, Vector2 to, Fix64 padding,
        Fix64 halfWidth, Fix64 halfHeight, ReadOnlySpan<FixedRectangleEdges2D> rectangles)
    {
        using var kernelSample = SceneRuntimeDiagnostics.MeasureKernel(SceneRuntimeDiagnostics.KernelKind.RectangleSegmentClear);
        if (Mathf.Abs(from.x) + padding >= halfWidth || Mathf.Abs(to.x) + padding >= halfWidth ||
            Mathf.Abs(from.y) + padding >= halfHeight || Mathf.Abs(to.y) + padding >= halfHeight) return false;
        return !SegmentIntersectsAny(from, to, padding, rectangles);
    }

    public static bool CircleIntersectsAny(Vector2 point, Fix64 radius, ReadOnlySpan<FixedRectangleEdges2D> rectangles)
    {
        foreach (ref readonly var bounds in rectangles)
        {
            if (point.x < bounds.Left - radius || point.x > bounds.Right + radius ||
                point.y < bounds.Bottom - radius || point.y > bounds.Top + radius) continue;
            Fix64 x = Mathf.Clamp(point.x, bounds.Left, bounds.Right);
            Fix64 y = Mathf.Clamp(point.y, bounds.Bottom, bounds.Top);
            if ((point - new Vector2(x, y)).sqrMagnitude < radius * radius) return true;
        }
        return false;
    }

    public static bool SegmentIntersectsAny(Vector2 from, Vector2 to, Fix64 padding,
        ReadOnlySpan<FixedRectangleEdges2D> rectangles)
    {
        var delta = to - from;
        var minX = Mathf.Min(from.x, to.x); var maxX = Mathf.Max(from.x, to.x);
        var minY = Mathf.Min(from.y, to.y); var maxY = Mathf.Max(from.y, to.y);
        foreach (ref readonly var bounds in rectangles)
        {
            var left = bounds.Left - padding; var right = bounds.Right + padding;
            var bottom = bounds.Bottom - padding; var top = bounds.Top + padding;
            if (maxX < left || minX > right || maxY < bottom || minY > top) continue;
            Fix64 near = 0, far = 1;
            if (Slab(from.x, delta.x, left, right, ref near, ref far) &&
                Slab(from.y, delta.y, bottom, top, ref near, ref far)) return true;
        }
        return false;
    }

    private static bool Slab(Fix64 origin, Fix64 delta, Fix64 low, Fix64 high, ref Fix64 near, ref Fix64 far)
    {
        if (Mathf.Abs(delta) < (Fix64).00001) return origin >= low && origin <= high;
        var a = (low - origin) / delta; var b = (high - origin) / delta;
        if (a > b) (a, b) = (b, a);
        near = Mathf.Max(near, a); far = Mathf.Min(far, b);
        return near <= far;
    }
}
