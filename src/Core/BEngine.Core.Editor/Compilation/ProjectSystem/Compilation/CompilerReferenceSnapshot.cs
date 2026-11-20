using System.Collections.Concurrent;
using System.Security.Cryptography;
using BEngine.Platform;

namespace BEngine.ProjectSystem;

// One compilation invocation only. Versioned reference files are immutable inputs;
// reuse their bytes across nodes without weakening content-based build identities.
internal sealed class CompilerReferenceSnapshot : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<byte[]?>> _contents = new(StringComparer.Ordinal);
    private readonly FileUtility _files = EditorHostServices.FileSystem;
    private readonly PathUtility _paths = EditorHostServices.Paths;
    private readonly long _budget;
    private long _retained;

    internal CompilerReferenceSnapshot(long budget = 32 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budget);
        _budget = budget;
    }
    internal long RetainedBytes => Interlocked.Read(ref _retained);

    internal void AppendTo(IncrementalHash hash, string path)
    {
        if (Read(path) is { } bytes) { hash.AppendData(bytes); return; }
        using var stream = _files.OpenRead(path);
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            int read;
            while ((read = stream.Read(buffer.AsSpan(0, 64 * 1024))) != 0)
                hash.AppendData(buffer.AsSpan(0, read));
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
    }

    internal void CopyTo(string source, string destination)
    {
        if (Read(source) is { } bytes) _files.WriteAllBytes(destination, bytes);
        else _files.CopyFile(source, destination, true);
    }

    private byte[]? Read(string path)
    {
        var fullPath = _paths.GetFullPath(path);
        if (_contents.TryGetValue(fullPath, out var captured)) return captured.Value;
        return _contents.GetOrAdd(fullPath, static (key, owner) =>
            new Lazy<byte[]?>(() => owner.Capture(key), LazyThreadSafetyMode.ExecutionAndPublication), this).Value;
    }

    private byte[]? Capture(string path)
    {
        var length = _files.GetFileLength(path);
        if (length > _budget) return null;
        if (Interlocked.Add(ref _retained, length) > _budget)
        { Interlocked.Add(ref _retained, -length); return null; }
        byte[] bytes;
        try { bytes = _files.ReadAllBytes(path); }
        catch { Interlocked.Add(ref _retained, -length); throw; }
        if (Interlocked.Add(ref _retained, bytes.LongLength - length) <= _budget) return bytes;
        Interlocked.Add(ref _retained, -bytes.LongLength);
        return null;
    }

    public void Dispose() { _contents.Clear(); Interlocked.Exchange(ref _retained, 0); }
}
