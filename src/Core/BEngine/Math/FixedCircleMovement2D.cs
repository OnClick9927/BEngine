namespace BEngine;

/// <summary>Subdivided fixed point circle movement against immutable rectangle edges.</summary>
public static class FixedCircleMovement2D
{
    /// <summary>
    /// Moves in bounded steps, preferring the X slide before comparing Y progress.
    /// The caller supplies a positive step distance and the squared minimum slide
    /// ratio for impact classification. Rectangle data is read only during this call.
    /// </summary>
    public static Vector2 Move(Vector2 from, Vector2 delta, Fix64 radius,
        ReadOnlySpan<FixedRectangleEdges2D> rectangles, Fix64 halfWidth, Fix64 halfHeight,
        Fix64 maximumStepDistance, Fix64 minimumSlideSquaredRatio,
        out bool hit, out bool impact)
    {
        using var kernelSample = SceneRuntimeDiagnostics.MeasureKernel(SceneRuntimeDiagnostics.KernelKind.CircleMovementMove);
        hit = false;
        impact = false;
        int count = Math.Max(1, (int)(delta.magnitude / maximumStepDistance) + 1);
        Vector2 step = delta / count;
        Fix64 minimumSlideSquared = step.sqrMagnitude * minimumSlideSquaredRatio;
        for (int i = 0; i < count; i++)
        {
            Vector2 next = from + step;
            if (!Blocked(next, radius, rectangles, halfWidth, halfHeight))
            {
                from = next;
                continue;
            }
            hit = true;
            Vector2 slide = Vector2.zero;
            Vector2 x = new(step.x, 0), y = new(0, step.y);
            if (step.x != 0 && !Blocked(from + x, radius, rectangles, halfWidth, halfHeight)) slide = x;
            if (Mathf.Abs(step.y) > Mathf.Abs(slide.x) &&
                !Blocked(from + y, radius, rectangles, halfWidth, halfHeight)) slide = y;
            from += slide;
            if (slide.sqrMagnitude < minimumSlideSquared) impact = true;
        }
        return from;
    }

    private static bool Blocked(Vector2 point, Fix64 radius,
        ReadOnlySpan<FixedRectangleEdges2D> rectangles, Fix64 halfWidth, Fix64 halfHeight)
    {
        if (Mathf.Abs(point.x) + radius > halfWidth || Mathf.Abs(point.y) + radius > halfHeight)
            return true;
        return FixedRectangleQueries2D.CircleIntersectsAny(point, radius, rectangles);
    }
}
