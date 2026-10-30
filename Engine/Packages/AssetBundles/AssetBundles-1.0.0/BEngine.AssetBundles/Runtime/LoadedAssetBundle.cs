using System.IO.Compression;
using System.Security.Cryptography;
using BEngine.Platform;

namespace BEngine.AssetBundles;

internal sealed class LoadedAssetBundle : IDisposable
{
    private readonly Stream _stream;
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries;
    private readonly Dictionary<string, byte[]> _assetCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _archiveGate = new();
    private readonly object _lifetimeGate = new();
    private readonly Dictionary<string, BTask<byte[]>> _pendingReads = new(StringComparer.OrdinalIgnoreCase);
    private int _activeReads;
    private int _references;
    private bool _disposed;

    private LoadedAssetBundle(
        string hash,
        Stream stream,
        ZipArchive archive,
        Dictionary<string, ZipArchiveEntry> entries)
    {
        Hash = hash;
        _stream = stream;
        _archive = archive;
        _entries = entries;
    }

    internal string Hash { get; }

    internal static async BValueTask<LoadedAssetBundle> OpenAsync(
        FileUtility fileSystem,
        string path,
        AssetBundleDescriptor descriptor,
        IReadOnlyList<AssetBundleAsset> assets,
        AssetBundleRuntimeOptions options,
        BCancellationToken cancellationToken,
        Action<long>? progress = null)
    {
        await AssetBundleFileVerifier.VerifyAsync(
            fileSystem, path, descriptor.Size, descriptor.Sha256,
            options.MaximumBundleSize, cancellationToken, progress);
        return OpenVerified(fileSystem, path, descriptor, assets, options);
    }

    internal static LoadedAssetBundle Open(
        FileUtility fileSystem,
        string path,
        AssetBundleDescriptor descriptor,
        IReadOnlyList<AssetBundleAsset> assets,
        AssetBundleRuntimeOptions options)
    {
        AssetBundleFileVerifier.Verify(
            fileSystem, path, descriptor.Size, descriptor.Sha256, options.MaximumBundleSize);
        return OpenVerified(fileSystem, path, descriptor, assets, options);
    }

    private static LoadedAssetBundle OpenVerified(
        FileUtility fileSystem,
        string path,
        AssetBundleDescriptor descriptor,
        IReadOnlyList<AssetBundleAsset> assets,
        AssetBundleRuntimeOptions options)
    {
        return OpenVerified(fileSystem.OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.RandomAccess), descriptor, assets, options);
    }

    // Owns the already size/hash-verified source, including when decryption or ZIP validation fails.
    internal static LoadedAssetBundle OpenVerified(
        Stream source,
        AssetBundleDescriptor descriptor,
        IReadOnlyList<AssetBundleAsset> assets,
        AssetBundleRuntimeOptions options)
    {
        using var measurement = AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.BundleOpen, descriptor.Size);
        var stream = source;
        ZipArchive? archive = null;
        try
        {
            stream = BEngine.Content.ContentEncryption.OpenRead(
                source, options.EncryptionKey, options.RequireEncryptedContent);
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var expected = new Dictionary<string, AssetBundleAsset>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in assets)
            {
                if (expected.TryGetValue(asset.Entry, out var existing))
                {
                    if (!asset.Entry.Equals(existing.Entry, StringComparison.Ordinal) ||
                        !asset.Sha256.Equals(existing.Sha256, StringComparison.OrdinalIgnoreCase) ||
                        asset.Size != existing.Size)
                        throw new InvalidDataException(
                            $"Asset bundle entry '{asset.Entry}' maps to conflicting catalog payloads.");
                    continue;
                }
                expected.Add(asset.Entry, asset);
            }
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                if (IsLink(entry))
                    throw new InvalidDataException($"Asset bundle links are not supported: '{entry.FullName}'.");
                if (!expected.TryGetValue(entry.FullName, out var asset) ||
                    !entry.FullName.Equals(asset.Entry, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"Unexpected asset bundle entry '{entry.FullName}' in '{descriptor.Name}'.");
                if (!entries.TryAdd(entry.FullName, entry))
                    throw new InvalidDataException($"Duplicate asset bundle entry '{entry.FullName}'.");
                if (entry.Length != asset.Size)
                    throw new InvalidDataException(
                        $"Asset bundle entry '{entry.FullName}' size does not match its catalog.");
                if (entry.Length > options.MaximumAssetSize)
                    throw new InvalidDataException($"Asset bundle entry '{entry.FullName}' exceeds its size limit.");
            }
            var missing = expected.Keys.FirstOrDefault(entry => !entries.ContainsKey(entry));
            if (missing is not null)
                throw new InvalidDataException($"Asset bundle entry '{missing}' is missing.");
            return new LoadedAssetBundle(
                descriptor.Sha256.ToLowerInvariant(), stream, archive, entries);
        }
        catch
        {
            archive?.Dispose();
            stream.Dispose();
            throw;
        }
    }

    internal bool TryAcquire()
    {
        if (_disposed) return false;
        checked { _references++; }
        return true;
    }

    internal void Release()
    {
        if (_references <= 0) return;
        _references--;
    }

    internal int ReferenceCount
    {
        get => _references;
    }

    internal async BValueTask<byte[]> ReadAssetAsync(
        AssetBundleAsset asset,
        long maximumSize,
        BCancellationToken cancellationToken,
        Action<long>? progress = null)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (_assetCache.TryGetValue(asset.Entry, out var bytes))
        { AssetBundlePerformanceDiagnostics.Count(AssetBundlePerformanceDiagnostics.Phase.CacheHit, bytes.Length); return bytes; }
        AssetBundlePerformanceDiagnostics.Count(AssetBundlePerformanceDiagnostics.Phase.CacheMiss, asset.Size);
        using var measurement = AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.AssetReadWall, asset.Size);
        // Web remains cooperative; no ThreadPool worker exists there. Desktop
        // and mobile workers only touch this bundle's private stream and data.
        if (!BackgroundWorkScheduler.IsSupported)
            return await ReadCooperatively(asset, maximumSize, cancellationToken, progress);
        if (_pendingReads.TryGetValue(asset.Entry, out var pending)) return await pending;
        var completion = new BTaskCompletionSource<byte[]>();
        _pendingReads.Add(asset.Entry, completion.Task);
        AcquireRead();
        try
        {
            long processed = 0;
            var reading = BackgroundWorkScheduler.Main.Run(token =>
            {
                lock (_archiveGate) return ReadAssetCore(asset, maximumSize, token,
                    progress is null ? null : count => Interlocked.Exchange(ref processed, count));
            }, cancellationToken);
            long previous = -1;
            while (progress is not null && !reading.IsCompleted)
            {
                var count = Interlocked.Read(ref processed);
                if (count != previous) { previous = count; progress(count); }
                await BTask.Yield();
            }
            bytes = await reading;
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            _assetCache[asset.Entry] = bytes;
            completion.TrySetResult(bytes);
            return bytes;
        }
        catch (BOperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); throw; }
        catch (Exception error) { completion.TrySetException(error); throw; }
        finally
        {
            _pendingReads.Remove(asset.Entry);
            ReleaseRead();
        }
    }

    internal byte[] ReadAsset(AssetBundleAsset asset, long maximumSize)
    {
        ThrowIfDisposed();
        if (_assetCache.TryGetValue(asset.Entry, out var bytes))
        { AssetBundlePerformanceDiagnostics.Count(AssetBundlePerformanceDiagnostics.Phase.CacheHit, bytes.Length); return bytes; }
        AssetBundlePerformanceDiagnostics.Count(AssetBundlePerformanceDiagnostics.Phase.CacheMiss, asset.Size);
        using var measurement = AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.AssetReadWall, asset.Size);
        AcquireRead();
        try { lock (_archiveGate) bytes = ReadAssetCore(asset, maximumSize, CancellationToken.None); }
        finally { ReleaseRead(); }
        ThrowIfDisposed(); _assetCache[asset.Entry] = bytes; return bytes;
    }

    private async BTask<byte[]> ReadCooperatively(AssetBundleAsset asset, long maximumSize, BCancellationToken token, Action<long>? progress)
    {
        // ZipArchive shares its underlying stream. A cooperative reader owns
        // it for the read; only a bounded byte block is decompressed per step.
        // Platforms with workers use the isolated archive gate above instead.
        if (_pendingReads.TryGetValue(asset.Entry, out var pending)) return await pending;
        var completion = new BTaskCompletionSource<byte[]>();
        _pendingReads.Add(asset.Entry, completion.Task);
        AcquireRead();
        try
        {
            if (!_entries.TryGetValue(asset.Entry, out var entry)) throw new FileNotFoundException(asset.Entry);
            if (asset.Size < 0 || asset.Size > maximumSize || asset.Size > int.MaxValue) throw new InvalidDataException("Asset size limit exceeded.");
            var bytes = new byte[(int)asset.Size];
            Stream opened;
            using (AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.AssetOpen)) opened = entry.Open();
            using var source = opened;
            var useNativeHash = asset.Size >= 256 * 1024 && BrowserAssetBundleHash.IsSupported(asset.Size);
            // Browser IncrementalHash buffers the whole payload until finalization.
            // Keep actual compression bounded by the same cooperative read block.
            var useStreamingHash = !useNativeHash && OperatingSystem.IsBrowser();
            using var streamingHash = useStreamingHash ? new AssetBundleStreamingSha256() : null;
            using var hash = useNativeHash || useStreamingHash ? null : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var offset = 0;
            var context = new AsyncLoadContext(token);
            await context.RunSteps(() =>
            {
                using var execution = AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.AssetReadExecute);
                ThrowIfDisposed(); token.ThrowIfCancellationRequested();
                var count = Math.Min(64 * 1024, bytes.Length - offset);
                if (count > 0)
                {
                    // Deflate streams may return only a few KiB per Read even
                    // when more output is immediately available. Fill this
                    // bounded block before yielding to the scheduler.
                    var start = offset;
                    var end = offset + count;
                    while (offset < end)
                    {
                        ThrowIfDisposed(); token.ThrowIfCancellationRequested();
                        int read;
                        using (AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.AssetInflate))
                            read = source.Read(bytes, offset, end - offset);
                        if (read == 0) throw new InvalidDataException("Asset bundle entry ended early.");
                        AssetBundlePerformanceDiagnostics.Count(AssetBundlePerformanceDiagnostics.Phase.AssetChunks, read);
                        if (hash is not null)
                            using (AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.AssetSha, read))
                                hash.AppendData(bytes, offset, read);
                        offset += read;
                    }
                    if (streamingHash is not null)
                        using (AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.AssetSha, offset - start))
                            streamingHash.Append(bytes.AsSpan(start, offset - start));
                    progress?.Invoke(offset);
                    if (offset < bytes.Length) return CooperativeWorkStatus.Continue;
                }
                if (source.ReadByte() != -1)
                    throw new InvalidDataException($"Asset '{asset.Address}' failed its content verification.");
                if (hash is not null || streamingHash is not null)
                {
                    string actualHash;
                    using (AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.AssetSha))
                    {
                        if (streamingHash is not null)
                        {
                            Span<byte> digest = stackalloc byte[32];
                            streamingHash.FinalizeHash(digest);
                            actualHash = Convert.ToHexString(digest);
                        }
                        else actualHash = Convert.ToHexString(hash!.GetHashAndReset());
                    }
                    if (!actualHash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Asset '{asset.Address}' failed its content verification.");
                }
                return CooperativeWorkStatus.Complete;
            });
            if (useNativeHash)
                using (AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.AssetSha, bytes.Length))
                    await BrowserAssetBundleHash.VerifyAsync(bytes, asset.Sha256, token);
            ThrowIfDisposed(); token.ThrowIfCancellationRequested();
            _assetCache[asset.Entry] = bytes; completion.TrySetResult(bytes); return bytes;
        }
        catch (BOperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); throw; }
        catch (Exception error) { completion.TrySetException(error); throw; }
        finally { _pendingReads.Remove(asset.Entry); ReleaseRead(); }
    }

    internal bool TryDisposeIfUnused()
    {
        if (_disposed || _references != 0) return false;
        Dispose();
        return true;
    }

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_disposed) return;
            _disposed = true;
            _assetCache.Clear();
            if (_activeReads == 0) DisposeResources();
        }
    }

    private byte[] ReadAssetCore(
        AssetBundleAsset asset,
        long maximumSize,
        CancellationToken cancellationToken,
        Action<long>? progress = null)
    {
        using var execution = AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.AssetReadExecute, asset.Size);
        if (!_entries.TryGetValue(asset.Entry, out var entry))
            throw new FileNotFoundException($"Asset bundle entry '{asset.Entry}' is missing.");
        if (asset.Size > maximumSize)
            throw new InvalidDataException($"Asset '{asset.Address}' exceeds its runtime size limit.");
        Stream opened;
        using (AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.AssetOpen)) opened = entry.Open();
        using var source = opened;
        if (asset.Size < 0 || asset.Size > int.MaxValue) throw new InvalidDataException("Asset size limit exceeded.");
        var output = new byte[(int)asset.Size];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var total = 0;
        while (total < output.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read;
            using (AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.AssetInflate))
                read = source.Read(output, total, Math.Min(64 * 1024, output.Length - total));
            if (read == 0) break;
            AssetBundlePerformanceDiagnostics.Count(AssetBundlePerformanceDiagnostics.Phase.AssetChunks, read);
            using (AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.AssetSha, read))
                hash.AppendData(output, total, read);
            total = checked(total + read);
            progress?.Invoke(total);
            if (total > asset.Size || total > maximumSize)
                throw new InvalidDataException($"Asset '{asset.Address}' exceeds its declared size.");
        }
        string actualHash;
        using (AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.AssetSha))
            actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (total != asset.Size || source.ReadByte() != -1 ||
            !actualHash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Asset '{asset.Address}' failed its content verification.");
        return output;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LoadedAssetBundle));
    }

    private void DisposeResources()
    {
        _archive.Dispose();
        _stream.Dispose();
    }

    private void AcquireRead()
    { lock (_lifetimeGate) { ThrowIfDisposed(); _activeReads++; } }
    private void ReleaseRead()
    { lock (_lifetimeGate) { if (--_activeReads == 0 && _disposed) DisposeResources(); } }

    private static bool IsLink(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
        var windowsAttributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
        return unixMode == 0xA000 || (windowsAttributes & FileAttributes.ReparsePoint) != 0;
    }
}
