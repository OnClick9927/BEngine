using System.Runtime.InteropServices;

namespace BEngine.Rendering.Rhi;

/// <summary>
/// Skips byte-identical dynamic mesh uploads using a bounded CPU snapshot.
/// All updates of the supplied mesh must go through this instance, or call Invalidate first.
/// The caller owns the mesh. This helper does not create threads or dispose the mesh.
/// </summary>
public sealed class GraphicsMeshUploadCache : IDisposable
{
    private readonly IGraphicsMesh _mesh;
    private byte[] _snapshot = [];
    private int _length;
    private bool _valid, _disposed;
    private int _maximumRetainedBytes = 8 * 1024 * 1024;

    public GraphicsMeshUploadCache(IGraphicsMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.Usage != GraphicsBufferUsage.Dynamic)
            throw new ArgumentException("Upload caching requires a dynamic mesh.", nameof(mesh));
        _mesh = mesh;
    }

    public int maximumRetainedBytes
    {
        get => _maximumRetainedBytes;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _maximumRetainedBytes = value;
            if (_snapshot.Length > value) Invalidate();
        }
    }
    public int retainedBytes => _snapshot.Length;
    public long uploadCount { get; private set; }
    public long uploadedBytes { get; private set; }
    public long skippedUploadCount { get; private set; }

    /// <returns>True when data was uploaded; false when the existing mesh already contains these bytes.</returns>
    public bool Update(ReadOnlySpan<float> vertices)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bytes = MemoryMarshal.AsBytes(vertices);
        if (_valid && bytes.Length == _length && bytes.SequenceEqual(_snapshot.AsSpan(0, _length)))
        { skippedUploadCount++; return false; }
        _valid = false;
        _mesh.Update(vertices);
        uploadCount++; uploadedBytes += bytes.Length;
        if (bytes.Length > _maximumRetainedBytes || _maximumRetainedBytes == 0)
        { Invalidate(); return true; }
        EnsureSnapshotCapacity(bytes.Length);
        bytes.CopyTo(_snapshot); _length = bytes.Length; _valid = true;
        return true;
    }

    // The core renderer already knows a changed projection rebuilt this stream.
    // Avoid comparing/copying a second CPU snapshot until that projection settles.
    // Keep its capacity so motion-to-rest does not allocate another large array.
    internal void UpdateRebuilt(ReadOnlySpan<float> vertices)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var byteCount = MemoryMarshal.AsBytes(vertices).Length;
        _valid = false;
        _mesh.Update(vertices);
        uploadCount++; uploadedBytes += byteCount;
        if (byteCount > _maximumRetainedBytes || _maximumRetainedBytes == 0) Invalidate();
    }

    // Only an owner that still holds the exact bytes from its last successful
    // upload may establish this proof without uploading again. In particular,
    // capture before the owner starts rewriting that retained vertex stream.
    internal void CaptureUploadedSnapshot(ReadOnlySpan<float> vertices)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bytes = MemoryMarshal.AsBytes(vertices);
        _valid = false;
        if (bytes.Length > _maximumRetainedBytes || _maximumRetainedBytes == 0)
        { Invalidate(); return; }
        EnsureSnapshotCapacity(bytes.Length);
        bytes.CopyTo(_snapshot); _length = bytes.Length; _valid = true;
    }

    private void EnsureSnapshotCapacity(int requiredBytes)
    {
        if (_snapshot.Length >= requiredBytes) return;
        // The caller has already checked the active byte length against the
        // budget. Keep the first allocation exact, then amortize incremental
        // geometry growth without ever retaining more than that budget.
        long grown = _snapshot.Length + (long)_snapshot.Length / 2;
        int capacity = (int)Math.Min(_maximumRetainedBytes, Math.Max(requiredBytes, grown));
        _snapshot = new byte[capacity];
    }

    /// <summary>Releases the snapshot and forces the next Update to upload.</summary>
    public void Invalidate() { _snapshot = []; _length = 0; _valid = false; }
    public void Dispose() { Invalidate(); _disposed = true; }
}
