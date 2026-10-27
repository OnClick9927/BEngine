namespace BEngine.Audio;

public readonly record struct AudioLoadingSnapshot(long ReservedPcmBytes, long LoadedPcmBytes,
    long PeakReservedPcmBytes, int PendingRequests, int ActiveDecoders, long RejectedLoads);

/// <summary>Process-wide, thread-safe PCM admission and decoder limits. Pending engine requests remain owner-thread managed.</summary>
public static class AudioLoadingSettings
{
    private static long _maximumDecodedBytes = 256L * 1024 * 1024, _reserved, _loaded, _allocated, _reclaimable, _peak, _rejected;
    private static int _maximumActiveDecoders = 8, _pending, _active;
    private static readonly HashSet<IDisposable> Requests = [];
    private static System.Runtime.Loader.AssemblyLoadContext? _context;
    private static bool _subscribed;
    private static readonly object Gate = new();
    public static long memoryPressureCollections { get; private set; }
    /// <summary>Coalesces file loads by path, modification time, size and load transform. SetData detaches shared PCM.</summary>
    public static bool shareDecodedResources { get; set; } = true;
    /// <summary>Allows asynchronous platform decoders for encoded clips. Unsupported formats fall back to the managed decoder.</summary>
    public static bool usePlatformDecoding { get; set; } = true;
    /// <summary>Opt-in loading-screen policy. Ordinary playback/loading never forces a blocking
    /// collection to admit PCM: reloadable clips use their Streaming fallback instead.</summary>
    public static bool allowBlockingCollectionOnPcmPressure { get; set; }
    public static long maximumDecodedBytes
    {
        get { lock (Gate) return _maximumDecodedBytes; }
        set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); lock (Gate) _maximumDecodedBytes = value; }
    }
    public static int maximumActiveDecoders
    {
        get { lock (Gate) return _maximumActiveDecoders; }
        set { if (value is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(value)); lock (Gate) _maximumActiveDecoders = value; }
    }
    // Leave space for the runtime, encoded data, scene objects and non-audio packages.
    public static long effectiveDecodedBudgetBytes
    {
        get
        {
            lock (Gate)
            {
            // Mono on Android can report zero for TotalAvailableMemoryBytes even
            // though allocations are valid. Treat an unavailable runtime value as
            // unknown and keep the explicit engine budget in force; otherwise the
            // first decoded clip is rejected with a misleading zero-byte budget.
            long heap;
            try { heap = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes; }
            catch (PlatformNotSupportedException) { heap = 0; }
            catch (InvalidOperationException) { heap = 0; }
            if (heap <= 0 || heap == long.MaxValue)
                return Math.Min(_maximumDecodedBytes, 128L * 1024 * 1024);
            var nonPcm = Math.Max(0, GC.GetTotalMemory(false) - _allocated);
            return Math.Min(_maximumDecodedBytes, Math.Max(0, heap - Math.Min(96L * 1024 * 1024, heap / 4) - nonPcm));
            }
        }
    }
    public static AudioLoadingSnapshot snapshot { get { lock (Gate) return new(_reserved, _loaded, _peak, _pending, _active, _rejected); } }
    internal static PcmReservation Reserve(long bytes, bool allowCollection = true)
    {
        lock (Gate)
        {
        var budget = effectiveDecodedBudgetBytes;
        if (allowCollection && allowBlockingCollectionOnPcmPressure && bytes > budget - _reserved && bytes <= _maximumDecodedBytes - _reserved && _reclaimable > 0)
        {
            // A released LOH array is still counted by GetTotalMemory until GC runs.
            // Reclaim only on actual admission pressure, never on ordinary frame updates.
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
            _reclaimable = 0; memoryPressureCollections++; budget = effectiveDecodedBudgetBytes;
        }
        if (bytes < 0 || bytes > budget - _reserved)
        {
            _rejected++;
            throw new PcmBudgetExceededException(bytes, _reserved, budget);
        }
        _reserved += bytes; _peak = Math.Max(_peak, _reserved);
        return new PcmReservation(bytes);
        }
    }
    internal static void AddPending(IDisposable request)
    {
        Requests.Add(request); lock (Gate) _pending++;
        if (_subscribed) return;
        _subscribed = true; Application.quitting += CancelPending;
        _context = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(AudioLoadingSettings).Assembly);
        if (_context?.IsCollectible == true) _context.Unloading += Unloading;
    }
    internal static void RemovePending(IDisposable request)
    {
        Requests.Remove(request); lock (Gate) _pending--;
        if (_pending != 0 || !_subscribed) return;
        _subscribed = false; Application.quitting -= CancelPending;
        if (_context?.IsCollectible == true) _context.Unloading -= Unloading;
        _context = null;
    }
    private static void Unloading(System.Runtime.Loader.AssemblyLoadContext _) => CancelPending();
    public static void CancelPending() { foreach (var request in Requests.ToArray()) request.Dispose(); }
    internal static bool TryOpenDecoder() { lock (Gate) { if (_active >= _maximumActiveDecoders) return false; _active++; return true; } }
    internal static void CloseDecoder() { lock (Gate) _active--; }
    internal sealed class PcmBudgetExceededException(long requested, long reserved, long budget) : InvalidOperationException(
        $"Audio PCM budget exceeded: requested {requested} bytes, reserved {reserved}, budget {budget}. Unload unused clips or use Streaming.");
    internal sealed class PcmReservation(long bytes) : IDisposable
    {
        private long _bytes = bytes;
        private bool _committed, _hasArray;
        internal void Allocated() { lock (Gate) { if (_hasArray) return; _hasArray = true; _allocated += _bytes; } }
        internal void Commit() { lock (Gate) { if (_committed) return; _committed = true; _loaded += _bytes; } }
        public void Dispose() { lock (Gate) { _reserved -= _bytes; if (_committed) _loaded -= _bytes; if (_hasArray) { _allocated -= _bytes; _reclaimable += _bytes; } _bytes = 0; } }
    }
}
