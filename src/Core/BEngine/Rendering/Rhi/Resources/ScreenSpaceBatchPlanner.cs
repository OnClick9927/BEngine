namespace BEngine.Rendering.Rhi;

/// <summary>A fixed-point screen rectangle before scissor conversion.</summary>
public readonly struct ScreenSpaceBatchRect(Fix64 x, Fix64 y, Fix64 width, Fix64 height)
{
    public readonly Fix64 X = x, Y = y, Width = width, Height = height;
}

/// <summary>
/// Plans ordered screen batches with a conservative one-pixel fringe. Only the
/// last twelve batches can be revisited, so retained state has a fixed bound
/// regardless of the number of paints or batches in a frame.
/// </summary>
public sealed class ScreenSpaceBatchPlanner
{
    private const int SearchLimit = 12;
    private readonly Entry[] _recent = new Entry[SearchLimit];
    private int _batchCount;

    /// <summary>Starts a new ordered stream and releases retained texture references.</summary>
    public void Reset()
    {
        Array.Clear(_recent);
        _batchCount = 0;
    }

    /// <summary>
    /// Returns the batch index for the next paint. An index equal to the prior
    /// number of batches creates a new batch; the caller owns geometry and draws.
    /// </summary>
    public int FindOrAdd(ScreenSpaceBatchRect clip, IGraphicsTexture2D? texture,
        ScreenSpaceBatchRect bounds, float? sdfThreshold = null, bool disjoint = true)
    {
        bounds = ClipBounds(bounds, clip);
        var limit = disjoint ? Math.Max(0, _batchCount - SearchLimit) : _batchCount - 1;
        for (var index = _batchCount - 1; index >= limit && index >= 0; index--)
        {
            ref var candidate = ref _recent[index % SearchLimit];
            if (SameRect(candidate.Clip, clip) && ReferenceEquals(candidate.Texture, texture) &&
                candidate.Threshold == sdfThreshold)
            {
                var previous = candidate.Bounds;
                var left = Fix64.Min(previous.X, bounds.X);
                var top = Fix64.Min(previous.Y, bounds.Y);
                candidate.Bounds = new ScreenSpaceBatchRect(left, top,
                    Fix64.Max(previous.X + previous.Width, bounds.X + bounds.Width) - left,
                    Fix64.Max(previous.Y + previous.Height, bounds.Y + bounds.Height) - top);
                return index;
            }
            if (Intersects(candidate.Bounds, bounds)) break;
        }

        var created = _batchCount;
        _recent[created % SearchLimit] = new Entry(clip, texture, sdfThreshold, bounds);
        _batchCount = checked(created + 1);
        return created;
    }

    private static bool SameRect(ScreenSpaceBatchRect left, ScreenSpaceBatchRect right) =>
        left.X == right.X && left.Y == right.Y && left.Width == right.Width && left.Height == right.Height;

    private static ScreenSpaceBatchRect ClipBounds(ScreenSpaceBatchRect bounds, ScreenSpaceBatchRect clip)
    {
        // Preserve the paint path's fixed-point operation and overflow order.
        var left = Fix64.Max(bounds.X - 1, clip.X - 1);
        var top = Fix64.Max(bounds.Y - 1, clip.Y - 1);
        var right = Fix64.Min(bounds.X + bounds.Width + 1, clip.X + clip.Width + 1);
        var bottom = Fix64.Min(bounds.Y + bounds.Height + 1, clip.Y + clip.Height + 1);
        return new(left, top, Fix64.Max(0, right - left), Fix64.Max(0, bottom - top));
    }

    private static bool Intersects(ScreenSpaceBatchRect left, ScreenSpaceBatchRect right) =>
        left.X < right.X + right.Width && right.X < left.X + left.Width &&
        left.Y < right.Y + right.Height && right.Y < left.Y + left.Height;

    private struct Entry(ScreenSpaceBatchRect clip, IGraphicsTexture2D? texture,
        float? threshold, ScreenSpaceBatchRect bounds)
    {
        internal ScreenSpaceBatchRect Clip = clip, Bounds = bounds;
        internal IGraphicsTexture2D? Texture = texture;
        internal float? Threshold = threshold;
    }
}
