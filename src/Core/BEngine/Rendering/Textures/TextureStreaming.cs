namespace BEngine.Rendering;

/// <summary>Opt-in mip residency. Requests use original texture levels (zero is full resolution).</summary>
public static class TextureStreaming
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> Explicit = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (long Frame, float Pixels)> Demands = new(StringComparer.Ordinal);
    private static long _lastFrame = -1, _lastPruned = -1, _residentBytes;
    public const int MaximumProjectedRequests = 16384;
    public static int projectedRequestCount { get { lock (Gate) return Demands.Count; } }
    public static int explicitRequestCount { get { lock (Gate) return Explicit.Count; } }
    /// <summary>Requested payload bytes currently resident in all live streaming caches.</summary>
    public static long residentMemoryBytes => Interlocked.Read(ref _residentBytes);
    internal static void AdjustResidentBytes(long delta) => Interlocked.Add(ref _residentBytes, delta);
    public static bool enabled { get; set; }
    /// <summary>Total scene texture admission budget across live caches; retired GPU resources remain charged.</summary>
    public static long memoryBudgetBytes { get => GraphicsTextureMemory.Budget.LimitBytes; set => GraphicsTextureMemory.Budget.LimitBytes = value; }
    public static int mipBias { get; set; }
    public static void RequestMipLevel(string source, int level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source); if (level < 0) throw new ArgumentOutOfRangeException(nameof(level));
        lock (Gate) Explicit[source] = level;
    }
    public static void ClearRequest(string source) { lock (Gate) { Explicit.Remove(source); Demands.Remove(source); } }
    internal static void ForgetProjection(string source) { lock (Gate) Demands.Remove(source); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ClearAllRequests() { lock (Gate) { Explicit.Clear(); Demands.Clear(); _lastFrame = _lastPruned = -1; } }
    public static void ReportProjectedSize(string source, float pixels)
    {
        if (!enabled || !float.IsFinite(pixels) || pixels <= 0) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        lock (Gate)
        {
            var frame = Time.frameCount;
            if (frame < _lastFrame) { Demands.Clear(); _lastPruned = frame; }
            if (_lastPruned < 0 || frame - _lastPruned >= 120)
            {
                foreach (var key in Demands.Where(pair => frame - pair.Value.Frame > 120).Select(pair => pair.Key).ToArray()) Demands.Remove(key);
                _lastPruned = frame;
            }
            _lastFrame = frame;
            if (Demands.Count >= MaximumProjectedRequests && !Demands.ContainsKey(source)) return;
            Demands[source] = Demands.TryGetValue(source, out var previous) && previous.Frame == frame ? (frame, Math.Max(previous.Pixels, pixels)) : (frame, pixels);
        }
    }
    internal static int DesiredMip(string source, int width, int height, int count)
    {
        lock (Gate)
        {
            if (Explicit.TryGetValue(source, out var level)) return Math.Clamp(level, 0, count - 1);
            var pixels = Demands.TryGetValue(source, out var demand) && Time.frameCount - demand.Frame is >= 0 and <= 2 ? demand.Pixels : Math.Max(width, height);
            return Math.Clamp((int)Math.Floor(Math.Log2(Math.Max(width, height) / Math.Max(1, pixels))) + mipBias, 0, count - 1);
        }
    }
}
