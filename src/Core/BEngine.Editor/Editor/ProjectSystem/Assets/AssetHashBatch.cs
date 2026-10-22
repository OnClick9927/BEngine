using System.Buffers;
using System.Security.Cryptography;
using BEngine.Platform;

namespace BEngine.ProjectSystem.Editor;

/// <summary>Actual hash reads and in-batch reuse; excludes importer and metadata I/O.</summary>
public readonly record struct AssetHashStatistics(long FileReads, long BytesRead, long CacheHits);

// One preparation/import transaction only. A new Refresh always re-reads contents,
// including files changed without a timestamp or length change.
internal sealed class AssetHashBatch(FileUtility files, PathUtility paths, CancellationToken cancellation) : IDisposable
{
    private readonly record struct Entry(long Length, DateTime Stamp, string Hash);
    private readonly Dictionary<string, Entry> _hashes = new(StringComparer.Ordinal);
    private byte[]? _buffer;
    private long _reads, _bytes, _hits;
    internal AssetHashStatistics Statistics => new(_reads, _bytes, _hits);
    internal string Compute(string path)
    {
        cancellation.ThrowIfCancellationRequested();
        path = paths.GetFullPath(path);
        var length = files.GetFileLength(path); var stamp = files.GetLastWriteTimeUtc(path);
        if (_hashes.TryGetValue(path, out var cached) && cached.Length == length && cached.Stamp == stamp)
        { _hits++; return cached.Hash; }
        using var stream = files.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        _buffer ??= ArrayPool<byte>.Shared.Rent(64 * 1024);
        _reads++;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var read = stream.Read(_buffer.AsSpan(0,64 * 1024));
            if (read == 0) break;
            _bytes += read; hash.AppendData(_buffer.AsSpan(0,read));
        }
        var result = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        _hashes[path] = new(length,stamp,result); return result;
    }
    // Importers can normalize source documents or create/replace dependencies.
    // Their writes may preserve timestamps, so invalidate the entire transaction cache.
    internal void Invalidate() => _hashes.Clear();
    public void Dispose()
    {
        _hashes.Clear();
        if (_buffer is { } buffer) { _buffer=null; ArrayPool<byte>.Shared.Return(buffer); }
    }
}
