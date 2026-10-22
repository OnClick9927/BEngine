namespace BEngine.Rendering.Rhi;

public readonly record struct GraphicsPassTiming(long Frame, GraphicsBackend Backend, string Name, double Milliseconds);
public interface IGraphicsPassTimingSource
{
    IDisposable? BeginGpuPass(string name);
}

public static class GraphicsPassTimingService
{
    private static readonly object Gate = new(); private static readonly Queue<GraphicsPassTiming> Samples = new();
    private static long _version;
    public static long Version { get { lock (Gate) return _version; } }
    public static GraphicsPassTiming[] Snapshot() => Snapshot(out _);
    /// <summary>Reads samples and their publication version atomically for incremental diagnostic consumers.</summary>
    public static GraphicsPassTiming[] Snapshot(out long version)
    {
        lock (Gate) { version = _version; return Samples.ToArray(); }
    }
    public static void Report(GraphicsPassTiming timing)
    {
        if (!double.IsFinite(timing.Milliseconds) || timing.Milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(timing));
        lock (Gate) { Samples.Enqueue(timing); while (Samples.Count > 512) Samples.Dequeue(); _version++; }
    }
}
