using BEngine.Rendering.Rhi;
using BEngine.Platform;
using System.Runtime.CompilerServices;

namespace BEngine.Rendering;

public sealed partial class SceneTextureCache : IDisposable
{
    private static readonly byte[] MissingPixels =
    [
        44, 46, 51, 255, 220, 62, 92, 255,
        220, 62, 92, 255, 44, 46, 51, 255
    ];

    private readonly IGraphicsDevice _device;
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _paths;
    private readonly PlatformUtility _platform;
    private readonly SharedState _shared;
    private readonly HashSet<string> _ownedSources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingPrepare> _pendingPrepares = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FailedPrepare> _failedPrepares = new(StringComparer.Ordinal);
    private Dictionary<string, CacheEntry> _textures => _shared.Textures;
    private IGraphicsTexture2D? _missing { get => _shared.Missing; set => _shared.Missing = value; }
    private bool _disposed;
    private long _frameSharedGeneration;
    private static readonly ConditionalWeakTable<IGraphicsDevice, ConditionalWeakTable<FileUtility, SharedState>> SharedStates = new();
    private static readonly List<WeakReference<SharedState>> LiveStates = [];
    static SceneTextureCache() => Application.lowMemory += TrimUnused;
    private readonly Dictionary<string, IGraphicsTexture2D> _frameTextures = new(StringComparer.Ordinal);
    private bool _frameCaching;
    private long _frameResourceGeneration;
    private bool _frameStreaming;
    /// <summary>Starts a texture resolution scope. File changes become visible on the next scope.</summary>
    public void BeginFrame() { _frameCaching = true; _frameTextures.Clear(); _frameResourceGeneration = BAsset.CacheGeneration; _frameStreaming = TextureStreaming.enabled; _frameSharedGeneration = _shared.Generation; }
    private StreamingTextureCache? _streaming { get => _shared.Streaming; set => _shared.Streaming = value; }
    private Dictionary<string, (DateTime Stamp, bool Container)> _streamable => _shared.Streamable;

    public SceneTextureCache(
        IGraphicsDevice device,
        FileUtility? fileSystem = null,
        PathUtility? paths = null,
        PlatformUtility? platform = null)
    {
        _device = device;
        _fileSystem = fileSystem ?? RuntimeServiceAccess.FileSystem;
        _paths = paths ?? RuntimeServiceAccess.PathService;
        _platform = platform ?? RuntimeServiceAccess.PlatformService;
        _shared = SharedStates.GetValue(device, static _ => new()).GetValue(_fileSystem, _ =>
        {
            var state = new SharedState(device); LiveStates.Add(new(state)); return state;
        });
        _shared.Owners++;
        LiveCaches.Add(new(this));
    }

    public IGraphicsTexture2D Resolve(string source)
    {
        var texture = ResolveCore(source);
        if (!string.IsNullOrWhiteSpace(source) && !source.StartsWith("memory-texture:", StringComparison.Ordinal) &&
            !source.StartsWith("missing:", StringComparison.Ordinal))
            _resolvedFrames[source] = BTaskScheduler.Main.FrameCount;
        if (!string.IsNullOrWhiteSpace(source) &&
            (_textures.ContainsKey(source) || _streaming?.Contains(source) == true) && _ownedSources.Add(source))
            _shared.SourceOwners[source] = _shared.SourceOwners.GetValueOrDefault(source) + 1;
        return texture;
    }

    /// <summary>Prepares one texture during loading. File reading and CPU decoding use bounded workers;
    /// the indivisible graphics creation is committed under the shared frame budget.</summary>
    public async BTask<bool> PrepareAsync(string source, AsyncLoadContext? context = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        context ??= new AsyncLoadContext();
        context.cancellationToken.ThrowIfCancellationRequested();
        if (source.StartsWith("memory-texture:", StringComparison.Ordinal)) { Resolve(source); return true; }
        var imported = TextureImportResolver.Resolve?.Invoke(source);
        var path = imported?.ArtifactPath ?? ResolvePath(source);
        var stamp = SourceStamp.Read(path, _fileSystem) with { ResourceGeneration = BAsset.CacheGeneration,
            Filter = imported?.Filter ?? TextureFilterMode.Bilinear, Wrap = imported?.Wrap ?? TextureWrapMode.Clamp };
        if (_textures.TryGetValue(source, out var resident) && resident.Stamp == stamp)
        { Resolve(source); return true; }
        byte[]? bytes;
        if (path is not null)
        {
            using var stream = _fileSystem.OpenRead(path);
            bytes = await context.ReadAllBytesAsync(stream);
        }
        else bytes = await ResourceLoader.LoadAsync(source, typeof(byte[]), "Resources", context) as byte[];
        if (bytes is null || bytes.Length == 0) return false;
        using var nativeImage = _device is IGraphicsEncodedImagePreparation nativeDecoder
            ? await nativeDecoder.PrepareEncodedImageAsync(bytes, context) : null;
        context.cancellationToken.ThrowIfCancellationRequested();
        var prepared = default(TextureUpload.PreparedTexture);
        if (nativeImage is null)
        {
            var image = BackgroundWorkScheduler.IsSupported
                ? await BackgroundWorkScheduler.Main.Run(token => { token.ThrowIfCancellationRequested(); return DecodeImage(bytes); }, context.cancellationToken)
                : DecodeImage(bytes);
            var supportsStored = TextureUpload.SupportsStoredFormat(_device, image.Format);
            prepared = BackgroundWorkScheduler.IsSupported
                ? await BackgroundWorkScheduler.Main.Run(token => { token.ThrowIfCancellationRequested(); return TextureUpload.Prepare(image, supportsStored); }, context.cancellationToken)
                : TextureUpload.Prepare(image, supportsStored);
        }
        else if (!ImageCodec.ValidSize(nativeImage.Width, nativeImage.Height) ||
                 nativeImage.ByteSize != (long)nativeImage.Width * nativeImage.Height * 4)
            throw new InvalidDataException("The device returned an invalid prepared image.");
        var sampling = imported is { } settings ? (settings.Filter, settings.Wrap)
            : path is not null ? BAssetReferenceLoader.ReadTextureSamplingSettings(path)
            : Resources.Load<Texture>(source) is { } metadata ? (metadata.filterMode, metadata.wrapMode)
            : (TextureFilterMode.Bilinear, TextureWrapMode.Clamp);
        var success = false;
        await context.RunSteps(() =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var currentSource = SourceStamp.Read(path, _fileSystem);
            if (stamp.ResourceGeneration != BAsset.CacheGeneration || currentSource.SourceWriteTimeUtc != stamp.SourceWriteTimeUtc ||
                currentSource.MetaWriteTimeUtc != stamp.MetaWriteTimeUtc)
                return CooperativeWorkStatus.Complete; // The source changed while the worker was reading it.
            if (_textures.TryGetValue(source, out var existing) && existing.Stamp == stamp)
            {
                existing.Touch(++_shared.Usage);
                if (_ownedSources.Add(source)) _shared.SourceOwners[source] = _shared.SourceOwners.GetValueOrDefault(source) + 1;
                success = true; return CooperativeWorkStatus.Complete;
            }
            if (_textures.Remove(source, out var previous)) Retire(previous);
            if (!Reserve(nativeImage?.ByteSize ?? prepared.Bytes.LongLength, out var reservation)) return CooperativeWorkStatus.Complete;
            IGraphicsTexture2D texture;
            try
            {
                var label = $"BEngine.Scene2D.{_paths.GetFileName(source)}";
                if (nativeImage is null) texture = TextureUpload.CreatePrepared(_device, label, prepared, sampling.Item1, sampling.Item2);
                else
                {
                    var filter = sampling.Item1 == TextureFilterMode.Point ? GraphicsTextureFilter.Nearest : GraphicsTextureFilter.Linear;
                    var wrap = sampling.Item2 == TextureWrapMode.Repeat ? GraphicsTextureAddressMode.Repeat
                        : sampling.Item2 == TextureWrapMode.Mirror ? GraphicsTextureAddressMode.MirroredRepeat : GraphicsTextureAddressMode.ClampToEdge;
                    texture = nativeImage.CreateTexture(label, new(nativeImage.Width, nativeImage.Height,
                        GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureUsage.Sampled, filter, filter, wrap));
                }
            }
            catch { reservation!.Dispose(); throw; }
            _textures[source] = new CacheEntry(stamp, texture, reservation, ++_shared.Usage);
            if (_ownedSources.Add(source)) _shared.SourceOwners[source] = _shared.SourceOwners.GetValueOrDefault(source) + 1;
            _shared.Generation++; success = true;
            return CooperativeWorkStatus.Complete;
        });
        return success;
    }

    private static TextureImageData DecodeImage(byte[] bytes)
    {
        if (TextureImageData.IsContainer(bytes)) return TextureImageData.Deserialize(bytes);
        if (!ImageCodec.TryDecode(bytes, out var width, out var height, out var rgba))
            throw new InvalidDataException("Unsupported texture image.");
        return TextureImageData.FromOwnedRgba(width, height, rgba);
    }

    private IGraphicsTexture2D ResolveCore(string source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(source) || source.StartsWith("missing:", StringComparison.Ordinal))
            return Missing();
        if (TryResolveRuntimeTexture(source, out var runtimeTexture)) return runtimeTexture;
        if (!_frameCaching) return ResolveSource(source);
        if (_frameResourceGeneration != BAsset.CacheGeneration || _frameStreaming != TextureStreaming.enabled || _frameSharedGeneration != _shared.Generation) BeginFrame();
        if (_frameTextures.TryGetValue(source, out var cachedFrame)) return cachedFrame;
        var resolved = ResolveSource(source); _frameTextures[source] = resolved; return resolved;
    }

    private IGraphicsTexture2D ResolveSource(string source)
    {
        // Bundled Player textures have no mutable source file. Resource
        // invalidation still changes their generation; runtime textures are
        // handled separately above with their pixel/sampler version.
        if (!Application.isEditor && !TextureStreaming.enabled && _streaming is null && _textures.TryGetValue(source, out var resident) &&
            resident.Stamp.ResourceGeneration == BAsset.CacheGeneration &&
            resident.Stamp.SourceWriteTimeUtc == DateTime.MinValue && resident.Stamp.MetaWriteTimeUtc == DateTime.MinValue)
        { resident.Touch(++_shared.Usage); return resident.Texture; }
        var importedSource = TextureImportResolver.Resolve?.Invoke(source);
        var path = importedSource?.ArtifactPath ?? ResolvePath(source);
        if (!TextureStreaming.enabled && _streaming is not null) { _streaming.Dispose(); _streaming = null; }
        if (TextureStreaming.enabled && path is not null && IsStreamable(path))
        {
            if (_textures.Remove(source, out var fullTexture)) Retire(fullTexture);
            var sampling = importedSource is { } settings ? (settings.Filter, settings.Wrap) : BAssetReferenceLoader.ReadTextureSamplingSettings(path);
            _streaming ??= new StreamingTextureCache(_device, _fileSystem, () => _shared.Generation++);
            return _streaming.Resolve(source, path, sampling.Item1, sampling.Item2) ?? Missing();
        }
        var isGuidSubAsset = path is null && source.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) &&
                             source.Contains("#subasset=", StringComparison.OrdinalIgnoreCase);
        if (!isGuidSubAsset && !Texture.IsSupportedSourcePath(path ?? source)) return MissingSource(source, "unsupported source path: " + path);
        var stamp = SourceStamp.Read(path, _fileSystem) with { ResourceGeneration = BAsset.CacheGeneration,
            Filter = importedSource?.Filter ?? TextureFilterMode.Bilinear,
            Wrap = importedSource?.Wrap ?? TextureWrapMode.Clamp };
        if (_textures.TryGetValue(source, out var cached) && cached.Stamp == stamp)
        {
            if (_pendingPrepares.TryGetValue(source, out var completed) && completed.Task.IsCompleted)
                CompletePrepare(source, completed);
            cached.Touch(++_shared.Usage); return cached.Texture;
        }

        if (_pendingPrepares.TryGetValue(source, out var pending))
        {
            if (pending.Path != path || pending.Stamp != stamp)
            {
                CancelPrepare(source, pending);
                _failedPrepares.Remove(source);
            }
            else if (!pending.Task.IsCompleted) return Missing();
            else
            {
                CompletePrepare(source, pending);
                if (_textures.TryGetValue(source, out cached) && cached.Stamp == stamp)
                { cached.Touch(++_shared.Usage); return cached.Texture; }
                if (_failedPrepares.TryGetValue(source, out var failed) &&
                    failed.Stamp == stamp && failed.Generation == _shared.Generation)
                    return Missing();
            }
        }
        if (_failedPrepares.TryGetValue(source, out var previousFailure))
        {
            if (previousFailure.Stamp == stamp && previousFailure.Generation == _shared.Generation)
                return Missing();
            _failedPrepares.Remove(source);
        }
        // Resolve is synchronous by contract, so a first miss must never read
        // and decode a full texture on the render thread. PrepareAsync performs
        // file IO and CPU conversion asynchronously, then commits the GPU object
        // through its bounded cooperative step.
        StartPrepare(source, path, stamp);
        return Missing();
    }

    private void StartPrepare(string source, string? path, SourceStamp stamp)
    {
        if (_pendingPrepares.ContainsKey(source)) return;
        var cancellation = new BCancellationSource();
        try
        {
            var task = PrepareAsync(source, new AsyncLoadContext(cancellation.Token));
            _pendingPrepares[source] = new(path, stamp, cancellation, task);
        }
        catch (Exception error)
        {
            cancellation.Dispose();
            _failedPrepares[source] = new(stamp, _shared.Generation);
            MissingSource(source, error.Message);
        }
    }

    private bool CompletePrepare(string source, PendingPrepare pending)
    {
        _pendingPrepares.Remove(source);
        var success = false;
        try
        {
            if (success = pending.Task.GetAwaiter().GetResult()) _failedPrepares.Remove(source);
            else _failedPrepares[source] = new(pending.Stamp, _shared.Generation);
        }
        catch (BOperationCanceledException) { }
        catch (Exception error)
        {
            _failedPrepares[source] = new(pending.Stamp, _shared.Generation);
            MissingSource(source, error.Message);
        }
        finally { pending.Cancellation.Dispose(); }
        return success;
    }

    private void CancelPrepare(string source, PendingPrepare pending)
    {
        if (!_pendingPrepares.Remove(source)) return;
        pending.Cancellation.Cancel(); pending.Cancellation.Dispose();
    }

    private bool IsStreamable(string path)
    {
        var stamp = _fileSystem.GetLastWriteTimeUtc(path);
        if (_streamable.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Container;
        using var stream = _fileSystem.OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.None);
        Span<byte> signature = stackalloc byte[8]; var read = stream.Read(signature);
        var container = read == 8 && TextureImageData.IsContainer(signature); _streamable[path] = (stamp, container); return container;
    }

    public void PruneDestroyedRuntimeTextures()
    {
        BeginFrame();
        _staleRuntimeSources.Clear();
        foreach (var key in _ownedSources)
        {
            if (!key.StartsWith("memory-texture:", StringComparison.Ordinal)) continue;
            if (!int.TryParse(key.AsSpan("memory-texture:".Length), out var id) || BObject.FindObjectFromInstanceID(id) is not null) continue;
            _staleRuntimeSources.Add(key);
        }
        foreach (var key in _staleRuntimeSources)
        {
            _ownedSources.Remove(key); ReleaseSource(key);
            if (_textures.Remove(key, out var entry)) Retire(entry);
        }
        _staleRuntimeSources.Clear();
    }

    private readonly List<string> _staleRuntimeSources = [];
    private readonly HashSet<string> _missingReports = new(StringComparer.Ordinal);

    private IGraphicsTexture2D MissingSource(string source, string reason)
    {
        if (_missingReports.Add(source)) Debug.LogWarning($"Texture '{source}' is unavailable: {reason}.");
        return Missing();
    }

    private void Retire(CacheEntry entry)
    {
        GraphicsTextureMemory.Retire(_device, entry.Texture, entry.Reservation); _shared.Generation++;
    }
    private bool Reserve(long bytes, out MemoryBudget.Lease? lease)
    {
        if (GraphicsTextureMemory.TryReserve(bytes, out lease)) return true;
        if (_device is IGraphicsResourceRetirementMaintenance pending && pending.TryFlushRetiredResources() &&
            GraphicsTextureMemory.TryReserve(bytes, out lease)) return true;
        long retired = 0;
        var required = bytes - Math.Max(0, GraphicsTextureMemory.Budget.LimitBytes - GraphicsTextureMemory.reservedBytes);
        while (true)
        {
            string? oldestKey = null;
            CacheEntry? oldest = null;
            foreach (var pair in _textures)
            {
                if (pair.Value.LastFrame == BTaskScheduler.Main.FrameCount ||
                    oldest is not null && pair.Value.Used >= oldest.Used) continue;
                oldestKey = pair.Key;
                oldest = pair.Value;
            }
            if (oldest is null) break;
            var retiredBytes = oldest.Texture.Description.DataByteSize;
            _textures.Remove(oldestKey!); Retire(oldest);
            if (GraphicsTextureMemory.TryReserve(bytes, out lease)) return true;
            retired += retiredBytes;
            if (retired >= required) break;
        }
        if (retired > 0 && _device is IGraphicsResourceRetirementMaintenance maintenance && maintenance.TryFlushRetiredResources())
            return GraphicsTextureMemory.TryReserve(bytes, out lease);
        return false;
    }
    /// <summary>Memory-pressure trim: first retires old entries, repeated pressure also retires recent inactive entries.
    /// Textures used in the current frame and their GPU reservation remain valid.</summary>
    public static void TrimUnused()
    {
        var frame = BTaskScheduler.Main.FrameCount;
        // A low-memory callback can fan out across many devices/caches. Keep
        // one notification bounded so the next frame does not retire hundreds
        // of GPU objects at once.
        var remaining = 32;
        for (var index = LiveStates.Count - 1; index >= 0; index--)
        {
            if (remaining <= 0) break;
            if (LiveStates[index].TryGetTarget(out var state)) remaining -= state.TrimInactive(frame, remaining);
            else LiveStates.RemoveAt(index);
        }
    }
    /// <summary>Releases scene-owned texture caches after a host memory-pressure notification.</summary>
    public static void TrimAll()
    {
        for (var index = LiveStates.Count - 1; index >= 0; index--)
        {
            if (LiveStates[index].TryGetTarget(out var state)) state.Trim();
            else LiveStates.RemoveAt(index);
        }
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _resolvedFrames.Clear();
        PruneLiveCaches();
        _frameTextures.Clear(); _frameCaching = false;
        while (_pendingPrepares.Count > 0)
        {
            using var enumerator = _pendingPrepares.GetEnumerator();
            if (!enumerator.MoveNext()) break;
            var pair = enumerator.Current;
            CancelPrepare(pair.Key, pair.Value);
        }
        _failedPrepares.Clear();
        foreach (var source in _ownedSources)
            ReleaseSource(source);
        _ownedSources.Clear();
        if (--_shared.Owners == 0) _shared.Trim();
    }

    private void ReleaseSource(string source)
    {
        var owners = _shared.SourceOwners[source] - 1;
        if (owners > 0) { _shared.SourceOwners[source] = owners; return; }
        _shared.SourceOwners.Remove(source);
        if (_pendingPrepares.TryGetValue(source, out var pending)) CancelPrepare(source, pending);
        _failedPrepares.Remove(source);
        if (_textures.Remove(source, out var entry)) Retire(entry);
        _streaming?.Release(source);
    }

    private IGraphicsTexture2D Missing() => _missing ??= _device.CreateTexture2D(
        "BEngine.Scene2D.MissingTexture",
        new GraphicsTextureDescription(2, 2, GraphicsTextureFormat.Rgba8Unorm,
            GraphicsTextureUsage.Sampled, GraphicsTextureFilter.Nearest,
            GraphicsTextureFilter.Nearest, GraphicsTextureAddressMode.ClampToEdge), MissingPixels);

    private bool TryResolveRuntimeTexture(string source, out IGraphicsTexture2D texture)
    {
        texture = null!;
        const string prefix = "memory-texture:";
        if (!source.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(source.AsSpan(prefix.Length), out var instanceId) ||
            BObject.FindObjectFromInstanceID(instanceId) is not Texture runtime) return false;
        if (runtime is RenderTexture renderTexture && renderTexture.TryGetSampleTexture(_device, out texture))
            return true;
        var stamp = new SourceStamp(DateTime.MinValue, DateTime.MinValue, runtime.PixelVersion,
            Filter: runtime.filterMode, Wrap: runtime.wrapMode);
        if (_textures.TryGetValue(source, out var cached) && cached.Stamp == stamp)
        {
            cached.Touch(++_shared.Usage);
            texture = cached.Texture;
            return true;
        }
        if (!runtime.TryGetRuntimeRgba(out var pixels)) return false;
        if (_textures.Remove(source, out var previous)) Retire(previous);
        if (!Reserve((long)runtime.width * runtime.height * 4, out var reservation)) { texture = Missing(); return true; }
        try { texture = _device.CreateTexture2D($"BEngine.Scene2D.Runtime.{instanceId}",
            new GraphicsTextureDescription(runtime.width, runtime.height, GraphicsTextureFormat.Rgba8Unorm,
                GraphicsTextureUsage.Sampled,
                runtime.filterMode == TextureFilterMode.Point
                    ? GraphicsTextureFilter.Nearest
                    : GraphicsTextureFilter.Linear,
                runtime.filterMode == TextureFilterMode.Point
                    ? GraphicsTextureFilter.Nearest
                    : GraphicsTextureFilter.Linear,
                runtime.wrapMode switch
                {
                    TextureWrapMode.Repeat => GraphicsTextureAddressMode.Repeat,
                    TextureWrapMode.Mirror => GraphicsTextureAddressMode.MirroredRepeat,
                    _ => GraphicsTextureAddressMode.ClampToEdge
                }), pixels); }
        catch { reservation!.Dispose(); throw; }
        _textures[source] = new CacheEntry(stamp, texture, reservation, ++_shared.Usage);
        return true;
    }

    private string? ResolvePath(string source)
    {
        try
        {
            if (source.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) &&
                BAsset.Load<Texture>(source) is { } imported)
            {
                var importedPath = string.IsNullOrWhiteSpace(imported.artifactPath)
                    ? imported.sourcePath
                    : imported.artifactPath;
                if (!string.IsNullOrWhiteSpace(importedPath) && _fileSystem.FileExists(importedPath))
                    return _paths.GetFullPath(importedPath);
            }
            if (_paths.IsPathRooted(source))
            {
                var rooted = _paths.GetFullPath(source);
                return _fileSystem.FileExists(rooted) ? rooted : null;
            }
            var normalized = source.Replace('/', _paths.DirectorySeparatorChar);
            var current = _paths.GetFullPath(normalized);
            if (_fileSystem.FileExists(current)) return current;
            var dataPath = _paths.GetFullPath(Application.dataPath);
            if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith($"Assets{_paths.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                var root = _paths.GetFileName(dataPath).Equals("Assets", StringComparison.OrdinalIgnoreCase)
                    ? _paths.GetDirectoryName(dataPath) ?? _platform.CurrentDirectory
                    : _platform.CurrentDirectory;
                var assetPath = _paths.GetFullPath(_paths.Combine(root, normalized));
                return _fileSystem.FileExists(assetPath) ? assetPath : null;
            }
            var dataFile = _paths.GetFullPath(_paths.Combine(dataPath, normalized));
            return _fileSystem.FileExists(dataFile) ? dataFile : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private readonly record struct SourceStamp(
        DateTime SourceWriteTimeUtc,
        DateTime MetaWriteTimeUtc,
        int RuntimeVersion = 0,
        long ResourceGeneration = 0,
        TextureFilterMode Filter = TextureFilterMode.Bilinear,
        TextureWrapMode Wrap = TextureWrapMode.Clamp)
    {
        internal static SourceStamp Read(string? path, FileUtility fileSystem)
        {
            if (path is null) return default;
            var metaPath = path + ".meta";
            return new SourceStamp(
                fileSystem.GetLastWriteTimeUtc(path),
                fileSystem.FileExists(metaPath)
                    ? fileSystem.GetLastWriteTimeUtc(metaPath)
                    : DateTime.MinValue);
        }
    }

    private sealed record PendingPrepare(string? Path, SourceStamp Stamp,
        BCancellationSource Cancellation, BTask<bool> Task);
    private readonly record struct FailedPrepare(SourceStamp Stamp, long Generation);

    private sealed class CacheEntry(SourceStamp stamp, IGraphicsTexture2D texture, MemoryBudget.Lease? reservation, long used)
    {
        internal SourceStamp Stamp = stamp;
        internal IGraphicsTexture2D Texture = texture;
        internal MemoryBudget.Lease? Reservation = reservation;
        internal long Used = used;
        internal long LastFrame = BTaskScheduler.Main.FrameCount;
        internal void Touch(long used) { Used = used; LastFrame = BTaskScheduler.Main.FrameCount; }
    }
    private sealed class SharedState(IGraphicsDevice device)
    {
        internal readonly Dictionary<string, CacheEntry> Textures = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, int> SourceOwners = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, (DateTime Stamp, bool Container)> Streamable = new(StringComparer.Ordinal);
        internal StreamingTextureCache? Streaming;
        internal IGraphicsTexture2D? Missing;
        internal int Owners;
        internal long Usage, Generation;
        private long _lastPressureFrame = long.MinValue;
        internal int TrimInactive(long frame, int maximumEntries)
        {
            if (maximumEntries <= 0) return 0;
            var recent = _lastPressureFrame != long.MinValue && frame - _lastPressureFrame < 120;
            _lastPressureFrame = frame;
            var minimumIdleFrames = recent ? 1 : 120;
            var removed = 0;
            while (removed < maximumEntries)
            {
                string? oldestKey = null;
                CacheEntry? oldest = null;
                foreach (var pair in Textures)
                {
                    if (frame - pair.Value.LastFrame < minimumIdleFrames ||
                        oldest is not null && pair.Value.Used >= oldest.Used) continue;
                    oldestKey = pair.Key;
                    oldest = pair.Value;
                }
                if (oldest is null) break;
                if (Textures.Remove(oldestKey!) && oldest is { } entry)
                {
                    GraphicsTextureMemory.Retire(device, entry.Texture, entry.Reservation);
                    removed++;
                }
            }
            removed += Streaming?.TrimInactive(frame, minimumIdleFrames,
                Math.Max(0, maximumEntries - removed)) ?? 0;
            if (removed > 0) Generation++;
            return removed;
        }
        internal void Trim()
        {
            Streaming?.Dispose(); Streaming = null;
            foreach (var entry in Textures.Values) GraphicsTextureMemory.Retire(device, entry.Texture, entry.Reservation);
            Textures.Clear(); Streamable.Clear(); Generation++;
            if (Missing is { } missing) GraphicsTextureMemory.Retire(device, missing, null);
            Missing = null;
        }
    }
}
