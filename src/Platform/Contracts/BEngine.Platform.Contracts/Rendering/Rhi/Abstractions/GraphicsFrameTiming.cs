namespace BEngine.Rendering.Rhi;

/// <summary>A completed GPU frame measurement reported by a graphics backend.</summary>
public readonly record struct GraphicsFrameTiming(
    long Sequence,
    GraphicsBackend Backend,
    double Milliseconds,
    DateTimeOffset CompletedAtUtc,
    string Method);

/// <summary>
/// Frame-boundary hook implemented by desktop graphics devices. Hosts call it around rendering;
/// the backend decides how to obtain a completed GPU measurement.
/// </summary>
public interface IGraphicsFrameTimingSource
{
    bool SupportsGpuFrameTiming { get; }
    void BeginGpuFrameTiming();
    void EndGpuFrameTiming();
}

/// <summary>
/// Process-wide timing feed. Measurements are disabled until a diagnostics consumer holds a lease.
/// </summary>
public static class GraphicsFrameTimingService
{
    private static readonly Lock Gate = new();
    private static int _leaseCount;
    private static int _passLeaseCount;
    private static long _sequence;
    private static GraphicsFrameTiming _latest;

    public static bool IsEnabled => Volatile.Read(ref _leaseCount) > 0;
    /// <summary>Whether a consumer needs individual passes, which can disable draw merging.</summary>
    public static bool IsPassTimingEnabled => Volatile.Read(ref _passLeaseCount) > 0;

    public static GraphicsFrameTiming Latest
    {
        get { lock (Gate) return _latest; }
    }

    public static IDisposable Acquire() => AcquireCore(true);

    /// <summary>Measures whole GPU frames while retaining normal batching and draw merging.</summary>
    public static IDisposable AcquireFrameOnly() => AcquireCore(false);

    private static IDisposable AcquireCore(bool passes)
    {
        var lease = new TimingLease(passes);
        Interlocked.Increment(ref _leaseCount);
        if (passes) Interlocked.Increment(ref _passLeaseCount);
        return lease;
    }

    public static void Report(GraphicsBackend backend, double milliseconds, string method)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        lock (Gate)
            _latest = new GraphicsFrameTiming(
                ++_sequence,
                backend,
                milliseconds,
                DateTimeOffset.UtcNow,
                method.Trim());
    }

    private sealed class TimingLease(bool passes) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (passes) Interlocked.Decrement(ref _passLeaseCount);
            if (Interlocked.Decrement(ref _leaseCount) < 0)
                Interlocked.Exchange(ref _leaseCount, 0);
        }
    }
}
