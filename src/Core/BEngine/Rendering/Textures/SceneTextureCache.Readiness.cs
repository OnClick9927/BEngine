namespace BEngine.Rendering;

public sealed partial class SceneTextureCache
{
    private static readonly List<WeakReference<SceneTextureCache>> LiveCaches = [];
    private readonly Dictionary<string, long> _resolvedFrames = new(StringComparer.Ordinal);

    /// <summary>Waits for encoded textures already submitted by real renderers to become GPU resident.
    /// Call on the render/engine thread after warmup rendering, and continue advancing that thread's frame scheduler.
    /// Only caches that actually resolved each source at or after <paramref name="observedSinceFrame"/> are included;
    /// the default observation window is the current and previous two engine frames. Pass the frame at which warmup
    /// started to isolate its renderers from old scenes and editor previews. The observer set is fixed at entry.
    /// No cache, device, decoding task or upload is created. Every observed cache must finish its existing preparation
    /// and retain a texture matching the current source stamp and asset generation. Returns false for unobserved,
    /// failed, released, disposed or stale textures. Memory textures and streamed containers are outside this API's
    /// scope and return false. Caller cancellation stops only this wait, leaving shared preparations running.</summary>
    public static async BTask<bool> WaitForReadyAsync(IEnumerable<string> sources, AsyncLoadContext? context = null,
        long? observedSinceFrame = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        context ??= new AsyncLoadContext();
        var cancellation = context.cancellationToken;
        cancellation.ThrowIfCancellationRequested();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            if (source.StartsWith("memory-texture:", StringComparison.Ordinal) ||
                source.StartsWith("missing:", StringComparison.Ordinal)) return false;
            unique.Add(source);
        }
        if (unique.Count == 0) return true;
        var since = observedSinceFrame ?? Math.Max(0, BTaskScheduler.Main.FrameCount - 2);
        var observers = new List<(SceneTextureCache Cache, string Source)>();
        PruneLiveCaches();
        foreach (var source in unique)
        {
            var found = false;
            foreach (var weak in LiveCaches)
            {
                if (!weak.TryGetTarget(out var cache) || cache._disposed ||
                    !cache._resolvedFrames.TryGetValue(source, out var frame) || frame < since) continue;
                if (!cache._pendingPrepares.ContainsKey(source) && !cache._ownedSources.Contains(source) &&
                    !cache._textures.ContainsKey(source) && !cache._failedPrepares.ContainsKey(source)) continue;
                observers.Add((cache, source));
                found = true;
            }
            if (!found) return false;
        }
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var waiting = false;
            foreach (var (cache, source) in observers)
            {
                if (cache._disposed) return false;
                if (cache._pendingPrepares.TryGetValue(source, out var pending))
                {
                    if (!pending.Task.IsCompleted) { waiting = true; continue; }
                    if (!cache.CompletePrepare(source, pending)) return false;
                }
                var imported = TextureImportResolver.Resolve?.Invoke(source);
                var path = imported?.ArtifactPath ?? cache.ResolvePath(source);
                var stamp = SourceStamp.Read(path, cache._fileSystem) with
                {
                    ResourceGeneration = BAsset.CacheGeneration,
                    Filter = imported?.Filter ?? TextureFilterMode.Bilinear,
                    Wrap = imported?.Wrap ?? TextureWrapMode.Clamp
                };
                if (!cache._textures.TryGetValue(source, out var resident) || resident.Stamp != stamp) return false;
            }
            if (!waiting) return true;
            await BTask.Delay(TimeSpan.Zero, cancellation);
        }
    }

    private static void PruneLiveCaches()
    {
        for (var index = LiveCaches.Count - 1; index >= 0; index--)
            if (!LiveCaches[index].TryGetTarget(out var cache) || cache._disposed) LiveCaches.RemoveAt(index);
    }
}
