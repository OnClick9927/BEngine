namespace BEngine;

/// <summary>Mutable circle state for an ordered fixed point separation solve.</summary>
public struct CircleSeparationBody2D
{
    public Vector2 Position;
    public Fix64 Radius;
    public Fix64 SeparationRadius;
    public Fix64 Mobility;
    public bool IsActive;
}

/// <summary>
/// Reusable, bounded circle separation against immutable axis aligned walls.
/// Pair ordering, correction accumulation and movement subdivision are stable.
/// </summary>
public sealed class FixedCircleSeparationWorkspace2D
{
    public const int MaximumSupportedBodies = 512;
    private readonly CircleSeparationBody2D[] _bodies;
    private readonly Vector2[] _corrections;
    private readonly int[] _cellsX, _cellsY;
    private readonly CellPairWorkspace2D _pairs;

    public int Capacity => _bodies.Length;
    /// <summary>Fill the first count entries before Solve; only positions are changed.</summary>
    public Span<CircleSeparationBody2D> Bodies => _bodies;
    /// <summary>Corrections from the last solver pass, in the input order.</summary>
    public ReadOnlySpan<Vector2> Corrections => _corrections;

    public FixedCircleSeparationWorkspace2D(int capacity)
    {
        if ((uint)capacity > MaximumSupportedBodies) throw new ArgumentOutOfRangeException(nameof(capacity));
        _bodies = new CircleSeparationBody2D[capacity];
        _corrections = new Vector2[capacity];
        _cellsX = new int[capacity]; _cellsY = new int[capacity];
        _pairs = new CellPairWorkspace2D(capacity);
    }

    /// <summary>
    /// Runs four ordered separation passes. Cells are two units wide; only
    /// neighboring cells are considered. Positions are committed after each
    /// pass, using the supplied world half extents and immutable wall edges.
    /// Wall tangency is traversable; movement retains the X/Y sliding order.
    /// </summary>
    public void Solve(int count, ReadOnlySpan<FixedRectangleEdges2D> rectangles,
        Fix64 halfWidth, Fix64 halfHeight)
    {
        using var kernelSample = SceneRuntimeDiagnostics.MeasureKernel(SceneRuntimeDiagnostics.KernelKind.CircleSeparationSolve);
        if ((uint)count > (uint)Capacity) throw new ArgumentOutOfRangeException(nameof(count));
        for (int pass = 0; pass < 4; pass++)
        {
            Array.Clear(_corrections); bool overlapping = false;
            for (int i = 0; i < count; i++)
            {
                _cellsX[i] = Mathf.FloorToInt(_bodies[i].Position.x / 2);
                _cellsY[i] = Mathf.FloorToInt(_bodies[i].Position.y / 2);
            }
            using (LoopExecutionGuard.AllowIterations(_pairs.PairCapacity))
            {
            ReadOnlySpan<IndexPair2D> pairs;
            using (SceneRuntimeDiagnostics.MeasureKernel(SceneRuntimeDiagnostics.KernelKind.CircleSeparationPairBuild))
                pairs = _pairs.Build(_cellsX.AsSpan(0, count), _cellsY.AsSpan(0, count));
            using (SceneRuntimeDiagnostics.MeasureKernel(SceneRuntimeDiagnostics.KernelKind.CircleSeparationPairResolution))
            foreach (var pair in pairs)
            {
                int i = pair.First, j = pair.Second;
                ref readonly var a = ref _bodies[i]; ref readonly var b = ref _bodies[j];
                if (!a.IsActive || !b.IsActive) continue;
                Fix64 radius = a.SeparationRadius + b.SeparationRadius;
                Vector2 delta = b.Position - a.Position;
                // Within this bound both component squares, their sum and the
                // radius square fit Fix64. Outside it retain the original
                // arithmetic and its ordered overflow behavior.
                ulong dx = UnsignedMagnitude(delta.x.RawValue);
                ulong dy = UnsignedMagnitude(delta.y.RawValue);
                ulong r = UnsignedMagnitude(radius.RawValue);
                if ((dx | dy | r) < (1UL << 46) && (dx >= r || dy >= r)) continue;
                Fix64 squared = delta.sqrMagnitude;
                if (squared >= radius * radius || !LineClear(a.Position, b.Position, rectangles, halfWidth, halfHeight)) continue;
                Fix64 wa = a.Mobility, wb = b.Mobility, total = wa + wb;
                if (total == 0) { wa = wb = 1; total = 2; }
                Fix64 distance = Mathf.Sqrt(squared); Vector2 correction;
                if (distance > Fix64.FromRaw(429496L)) correction = delta * ((radius - distance + Fix64.FromRaw(4294967L)) / distance);
                else
                {
                    Fix64 angle = (Fix64)((i * 13 + j * 7) % 16) * Mathf.PI / 8;
                    correction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius + Fix64.FromRaw(4294967L));
                }
                if (wa == wb)
                {
                    // Q32.32 multiplication by one half is an arithmetic shift,
                    // including negative odd raw values. The product cannot overflow.
                    var halfCorrection = new Vector2(Fix64.FromRaw(correction.x.RawValue >> 1),
                        Fix64.FromRaw(correction.y.RawValue >> 1));
                    _corrections[i] -= halfCorrection;
                    _corrections[j] += halfCorrection;
                }
                else
                {
                    Fix64 share = wa / total;
                    _corrections[i] -= correction * share;
                    _corrections[j] += correction * (1 - share);
                }
                overlapping = true;
            }
            }
            if (!overlapping) break;
            using (SceneRuntimeDiagnostics.MeasureKernel(SceneRuntimeDiagnostics.KernelKind.CircleSeparationApplyCorrections))
            for (int i = 0; i < count; i++) if (_corrections[i].sqrMagnitude > 0)
                _bodies[i].Position = Move(_bodies[i].Position,
                    Vector2.ClampMagnitude(_corrections[i], Fix64.FromRaw(2576980377L)), _bodies[i].Radius,
                    rectangles, halfWidth, halfHeight, out _, out _);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static ulong UnsignedMagnitude(long value) =>
        value < 0 ? unchecked(0UL - (ulong)value) : (ulong)value;

    private static bool LineClear(Vector2 from, Vector2 to, ReadOnlySpan<FixedRectangleEdges2D> rectangles,
        Fix64 halfWidth, Fix64 halfHeight)
    {
        Fix64 padding = Fix64.FromRaw(515396075L);
        if (Mathf.Abs(from.x) + padding >= halfWidth || Mathf.Abs(to.x) + padding >= halfWidth ||
            Mathf.Abs(from.y) + padding >= halfHeight || Mathf.Abs(to.y) + padding >= halfHeight) return false;
        return !FixedRectangleQueries2D.SegmentIntersectsAny(from, to, padding, rectangles);
    }

    private static bool Blocked(Vector2 point, Fix64 radius, ReadOnlySpan<FixedRectangleEdges2D> rectangles,
        Fix64 halfWidth, Fix64 halfHeight)
    {
        if (Mathf.Abs(point.x) + radius > halfWidth || Mathf.Abs(point.y) + radius > halfHeight) return true;
        return FixedRectangleQueries2D.CircleIntersectsAny(point, radius, rectangles);
    }

    private static Vector2 Move(Vector2 from, Vector2 delta, Fix64 radius,
        ReadOnlySpan<FixedRectangleEdges2D> rectangles, Fix64 halfWidth, Fix64 halfHeight,
        out bool hit, out bool impact)
    {
        hit = false; impact = false;
        int count = Math.Max(1, (int)(delta.magnitude / Fix64.FromRaw(644245094L)) + 1);
        Vector2 step = delta / count;
        Fix64 minimumSlideSquared = step.sqrMagnitude * Fix64.FromRaw(1073741824L);
        for (int i = 0; i < count; i++)
        {
            Vector2 next = from + step;
            if (!Blocked(next, radius, rectangles, halfWidth, halfHeight)) { from = next; continue; }
            hit = true;
            Vector2 slide = Vector2.zero;
            Vector2 x = new(step.x, 0), y = new(0, step.y);
            if (step.x != 0 && !Blocked(from + x, radius, rectangles, halfWidth, halfHeight)) slide = x;
            if (Mathf.Abs(step.y) > Mathf.Abs(slide.x) && !Blocked(from + y, radius, rectangles, halfWidth, halfHeight)) slide = y;
            from += slide;
            if (slide.sqrMagnitude < minimumSlideSquared) impact = true;
        }
        return from;
    }
}
