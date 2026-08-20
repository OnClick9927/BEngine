using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;

namespace BEngine.AssetBundles;

internal sealed class LoadedAssetBundle : IDisposable
{
    private readonly object _referenceGate = new();
    private readonly FileStream _stream;
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries;
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _assetLoads =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _archiveGate = new(1, 1);
    private readonly CancellationToken _lifetimeToken;
    private int _references;
    private bool _disposed;

    private LoadedAssetBundle(
        string hash,
        FileStream stream,
        ZipArchive archive,
        Dictionary<string, ZipArchiveEntry> entries,
        CancellationToken lifetimeToken)
    {
        Hash = hash;
        _stream = stream;
        _archive = archive;
        _entries = entries;
        _lifetimeToken = lifetimeToken;
    }

    internal string Hash { get; }

    internal static async Task<LoadedAssetBundle> OpenAsync(
        string path,
        AssetBundleDescriptor descriptor,
        IReadOnlyList<AssetBundleAsset> assets,
        AssetBundleRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        await AssetBundleFileVerifier.VerifyAsync(
            path, descriptor.Size, descriptor.Sha256, options.MaximumBundleSize, cancellationToken)
            .ConfigureAwait(false);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        ZipArchive? archive = null;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var expected = assets.ToDictionary(asset => asset.Entry, StringComparer.OrdinalIgnoreCase);
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
                descriptor.Sha256.ToLowerInvariant(), stream, archive, entries, cancellationToken);
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
        lock (_referenceGate)
        {
            if (_disposed) return false;
            checked { _references++; }
            return true;
        }
    }

    internal void Release()
    {
        lock (_referenceGate)
        {
            if (_references <= 0) return;
            _references--;
        }
    }

    internal int ReferenceCount
    {
        get { lock (_referenceGate) return _references; }
    }

    internal async Task<byte[]> ReadAssetAsync(
        AssetBundleAsset asset,
        long maximumSize,
        CancellationToken cancellationToken)
    {
        Lazy<Task<byte[]>> lazy;
        Task<byte[]> task;
        lock (_referenceGate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LoadedAssetBundle));
            lazy = _assetLoads.GetOrAdd(asset.Entry, _ => new Lazy<Task<byte[]>>(
                () => ReadAssetCoreAsync(asset, maximumSize, _lifetimeToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
            task = lazy.Value;
        }
        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested &&
                                                 !task.IsCompleted)
        {
            throw;
        }
        catch
        {
            if (_assetLoads.TryGetValue(asset.Entry, out var current) &&
                ReferenceEquals(current, lazy))
                _assetLoads.TryRemove(asset.Entry, out _);
            throw;
        }
    }

    internal bool TryDisposeIfUnused()
    {
        lock (_referenceGate)
        {
            if (_disposed || _references != 0) return false;
            _disposed = true;
        }
        DisposeResources();
        return true;
    }

    public void Dispose()
    {
        lock (_referenceGate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        DisposeResources();
    }

    private async Task<byte[]> ReadAssetCoreAsync(
        AssetBundleAsset asset,
        long maximumSize,
        CancellationToken cancellationToken)
    {
        await _archiveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!_entries.TryGetValue(asset.Entry, out var entry))
                throw new FileNotFoundException($"Asset bundle entry '{asset.Entry}' is missing.");
            if (asset.Size > maximumSize)
                throw new InvalidDataException($"Asset '{asset.Address}' exceeds its runtime size limit.");
            using var source = entry.Open();
            using var output = asset.Size <= int.MaxValue
                ? new MemoryStream((int)asset.Size)
                : new MemoryStream();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total = checked(total + read);
                if (total > asset.Size || total > maximumSize)
                    throw new InvalidDataException($"Asset '{asset.Address}' exceeds its declared size.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (total != asset.Size ||
                !actualHash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Asset '{asset.Address}' failed its content verification.");
            return output.ToArray();
        }
        finally
        {
            _archiveGate.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_referenceGate)
            if (_disposed) throw new ObjectDisposedException(nameof(LoadedAssetBundle));
    }

    private void DisposeResources()
    {
        var tasks = _assetLoads.Values.Select(lazy => lazy.Value).ToArray();
        if (tasks.Length > 0)
        {
            try { Task.WhenAll(tasks).ConfigureAwait(false).GetAwaiter().GetResult(); }
            catch { }
        }
        try
        {
            _archiveGate.Wait();
            _archiveGate.Release();
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        _assetLoads.Clear();
        _archive.Dispose();
        _stream.Dispose();
        _archiveGate.Dispose();
    }

    private static bool IsLink(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
        var windowsAttributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
        return unixMode == 0xA000 || (windowsAttributes & FileAttributes.ReparsePoint) != 0;
    }
}
