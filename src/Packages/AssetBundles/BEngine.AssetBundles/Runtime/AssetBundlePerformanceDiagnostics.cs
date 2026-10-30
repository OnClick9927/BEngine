using System.Diagnostics;

namespace BEngine.AssetBundles;

/// <summary>Opt-in cumulative resource preparation measurements. Nested and concurrent durations overlap.</summary>
public static class AssetBundlePerformanceDiagnostics
{
    private static readonly string[] Names = ["catalog.decode", "catalog.validate", "catalog.index", "catalog.clone",
        "sha.buffer", "sha.stream", "sha.async.wall", "bundle.open", "bundle.http.copy", "bundle.http.sha",
        "asset.read.wall", "asset.read.execute", "asset.inflate", "asset.sha", "asset.chunks", "asset.cache.hit", "asset.cache.miss", "asset.open", "asset.copy", "version.decode", "pointer.decode",
        "catalog.bundleValidate", "catalog.assetValidate", "catalog.packageFingerprint"];
    private static readonly Counter[] Values = Names.Select(static _ => new Counter()).ToArray();
    private static bool _enabled = Environment.GetEnvironmentVariable("BENGINE_AB_DIAGNOSTICS") == "1";
    public static bool Enabled { get => Volatile.Read(ref _enabled); set => Volatile.Write(ref _enabled, value); }

    /// <summary>Creates a detached snapshot. Read at diagnostic boundaries, rather than once per resource.</summary>
    public static AssetBundlePerformanceSnapshot Snapshot
    {
        get
        {
            var counters = new AssetBundlePerformanceCounter[Values.Length];
            for (var index = 0; index < counters.Length; index++)
            {
                var value = Values[index];
                counters[index] = new(Names[index], Interlocked.Read(ref value.Calls), Interlocked.Read(ref value.Bytes),
                    Milliseconds(Interlocked.Read(ref value.Ticks)), Milliseconds(Interlocked.Read(ref value.MaximumTicks)));
            }
            return new(Enabled, counters);
        }
    }

    /// <summary>Resets measurements. Call only when no measured work is active.</summary>
    public static void Reset()
    {
        foreach (var value in Values)
        {
            Interlocked.Exchange(ref value.Calls, 0); Interlocked.Exchange(ref value.Bytes, 0);
            Interlocked.Exchange(ref value.Ticks, 0); Interlocked.Exchange(ref value.MaximumTicks, 0);
        }
    }

    internal static Measurement Measure(Phase phase, long bytes = 0) => Enabled ? new(phase, bytes) : default;
    internal static void Count(Phase phase, long bytes = 0) { if (Enabled) Record(phase, 0, bytes); }
    internal static byte[] CloneAssetBytes(byte[] bytes)
    {
        using var measurement = Measure(Phase.AssetCopy, bytes.Length);
        return (byte[])bytes.Clone();
    }
    internal static long RemainingBytes(Stream stream)
    {
        if (!Enabled) return 0;
        // Instrumentation must not make an otherwise readable stream depend on optional seeking metadata.
        try { return stream.CanSeek ? Math.Max(0, stream.Length - stream.Position) : 0; }
        catch (Exception error) when (error is NotSupportedException or IOException or InvalidOperationException) { return 0; }
    }
    private static double Milliseconds(long ticks) => (double)ticks * 1000 / Stopwatch.Frequency;
    private static void Record(Phase phase, long ticks, long bytes)
    {
        var counter = Values[(int)phase];
        Interlocked.Increment(ref counter.Calls); Interlocked.Add(ref counter.Ticks, ticks); Interlocked.Add(ref counter.Bytes, bytes);
        var maximum = Interlocked.Read(ref counter.MaximumTicks);
        while (ticks > maximum)
        {
            var previous = Interlocked.CompareExchange(ref counter.MaximumTicks, ticks, maximum);
            if (previous == maximum) break;
            maximum = previous;
        }
    }
    internal enum Phase { CatalogDecode, CatalogValidate, CatalogIndex, CatalogClone, ShaBuffer, ShaStream, ShaAsyncWall,
        BundleOpen, HttpCopy, HttpSha, AssetReadWall, AssetReadExecute, AssetInflate, AssetSha, AssetChunks, CacheHit, CacheMiss, AssetOpen, AssetCopy,
        VersionDecode, PointerDecode, CatalogBundleValidate, CatalogAssetValidate, CatalogPackageFingerprint }
    internal readonly struct Measurement : IDisposable
    {
        private readonly Phase _phase;
        private readonly long _bytes, _started;
        private readonly bool _active;
        internal Measurement(Phase phase, long bytes) { _phase = phase; _bytes = bytes; _started = Stopwatch.GetTimestamp(); _active = true; }
        public void Dispose() { if (_active) Record(_phase, Stopwatch.GetTimestamp() - _started, _bytes); }
    }
    private sealed class Counter { internal long Calls, Bytes, Ticks, MaximumTicks; }
}

public sealed record AssetBundlePerformanceSnapshot(bool Enabled, AssetBundlePerformanceCounter[] Counters);
public sealed record AssetBundlePerformanceCounter(string Name, long Calls, long Bytes, double TotalMilliseconds, double MaximumMilliseconds);
