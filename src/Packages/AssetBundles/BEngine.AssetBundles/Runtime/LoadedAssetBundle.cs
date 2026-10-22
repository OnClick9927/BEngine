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
        BCancellationToken cancellationToken)
    {
        await AssetBundleFileVerifier.VerifyAsync(
            fileSystem, path, descriptor.Size, descriptor.Sha256,
            options.MaximumBundleSize, cancellationToken);
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
        var stream = BEngine.Content.ContentEncryption.OpenRead(
            fileSystem.OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.RandomAccess), options.EncryptionKey, options.RequireEncryptedContent);
        ZipArchive? archive = null;
        try
        {
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
        BCancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (_assetCache.TryGetValue(asset.Entry, out var bytes)) return bytes;
        // Web remains cooperative; no ThreadPool worker exists there. Desktop
        // and mobile workers only touch this bundle's private stream and data.
        if (!BackgroundWorkScheduler.IsSupported)
            return await ReadCooperatively(asset, maximumSize, cancellationToken);
        if (_pendingReads.TryGetValue(asset.Entry, out var pending)) return await pending;
        var completion = new BTaskCompletionSource<byte[]>();
        _pendingReads.Add(asset.Entry, completion.Task);
        AcquireRead();
        try
        {
            bytes = await BackgroundWorkScheduler.Main.Run(token =>
            {
                lock (_archiveGate) return ReadAssetCore(asset, maximumSize, token);
            }, cancellationToken);
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
        if (_assetCache.TryGetValue(asset.Entry, out var bytes)) return bytes;
        AcquireRead();
        try { lock (_archiveGate) bytes = ReadAssetCore(asset, maximumSize, CancellationToken.None); }
        finally { ReleaseRead(); }
        ThrowIfDisposed(); _assetCache[asset.Entry] = bytes; return bytes;
    }

    private async BTask<byte[]> ReadCooperatively(AssetBundleAsset asset, long maximumSize, BCancellationToken token)
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
            using var source = entry.Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var offset = 0;
            var context = new AsyncLoadContext(token);
            await context.RunSteps(() =>
            {
                ThrowIfDisposed(); token.ThrowIfCancellationRequested();
                var count = Math.Min(64 * 1024, bytes.Length - offset);
                if (count > 0)
                {
                    var read = source.Read(bytes, offset, count);
                    if (read == 0) throw new InvalidDataException("Asset bundle entry ended early.");
                    hash.AppendData(bytes, offset, read); offset += read;
                    return CooperativeWorkStatus.Continue;
                }
                if (source.ReadByte() != -1 || !Convert.ToHexString(hash.GetHashAndReset()).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Asset '{asset.Address}' failed its content verification.");
                return CooperativeWorkStatus.Complete;
            });
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
        CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(asset.Entry, out var entry))
            throw new FileNotFoundException($"Asset bundle entry '{asset.Entry}' is missing.");
        if (asset.Size > maximumSize)
            throw new InvalidDataException($"Asset '{asset.Address}' exceeds its runtime size limit.");
        using var source = entry.Open();
        if (asset.Size < 0 || asset.Size > int.MaxValue) throw new InvalidDataException("Asset size limit exceeded.");
        var output = new byte[(int)asset.Size];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var total = 0;
        while (total < output.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = source.Read(output, total, Math.Min(64 * 1024, output.Length - total));
            if (read == 0) break;
            hash.AppendData(output, total, read);
            total = checked(total + read);
            if (total > asset.Size || total > maximumSize)
                throw new InvalidDataException($"Asset '{asset.Address}' exceeds its declared size.");
        }
        var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
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
