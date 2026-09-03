using System.IO.Compression;
using System.Security.Cryptography;

namespace BEngine.AssetBundles;

internal sealed class LoadedAssetBundle : IDisposable
{
    private readonly FileStream _stream;
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries;
    private readonly Dictionary<string, byte[]> _assetCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private int _references;
    private bool _disposed;

    private LoadedAssetBundle(
        string hash,
        FileStream stream,
        ZipArchive archive,
        Dictionary<string, ZipArchiveEntry> entries)
    {
        Hash = hash;
        _stream = stream;
        _archive = archive;
        _entries = entries;
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
        lock (_gate)
        {
            if (_disposed) return false;
            checked { _references++; }
            return true;
        }
    }

    internal void Release()
    {
        lock (_gate)
        {
            if (_references <= 0) return;
            _references--;
        }
    }

    internal int ReferenceCount
    {
        get { lock (_gate) return _references; }
    }

    internal Task<byte[]> ReadAssetAsync(
        AssetBundleAsset asset,
        long maximumSize,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (!_assetCache.TryGetValue(asset.Entry, out var bytes))
            {
                bytes = ReadAssetCore(asset, maximumSize, cancellationToken);
                _assetCache[asset.Entry] = bytes;
            }
            return Task.FromResult(bytes);
        }
    }

    internal bool TryDisposeIfUnused()
    {
        lock (_gate)
        {
            if (_disposed || _references != 0) return false;
            _disposed = true;
            DisposeResources();
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            DisposeResources();
        }
    }

    private byte[] ReadAssetCore(
        AssetBundleAsset asset,
        long maximumSize,
        CancellationToken cancellationToken)
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
            cancellationToken.ThrowIfCancellationRequested();
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            total = checked(total + read);
            if (total > asset.Size || total > maximumSize)
                throw new InvalidDataException($"Asset '{asset.Address}' exceeds its declared size.");
            hash.AppendData(buffer, 0, read);
            output.Write(buffer, 0, read);
        }
        var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (total != asset.Size ||
            !actualHash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Asset '{asset.Address}' failed its content verification.");
        return output.ToArray();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LoadedAssetBundle));
    }

    private void DisposeResources()
    {
        _assetCache.Clear();
        _archive.Dispose();
        _stream.Dispose();
    }

    private static bool IsLink(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
        var windowsAttributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
        return unixMode == 0xA000 || (windowsAttributes & FileAttributes.ReparsePoint) != 0;
    }
}
