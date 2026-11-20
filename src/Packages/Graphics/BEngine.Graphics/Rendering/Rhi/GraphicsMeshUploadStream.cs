namespace BEngine.Rendering.Rhi;

/// <summary>
/// Owns a reusable CPU vertex stream and its dynamic mesh upload history. Writable
/// spans are borrowed only within a WriteScope; they must not be retained across
/// another write, scope completion or upload. Instances are used synchronously by
/// one producer. The caller owns the attached mesh and must call Invalidate after
/// changing it outside this stream.
/// </summary>
public sealed class GraphicsMeshUploadStream : IDisposable
{
    private float[] _vertices = [];
    private int _count;
    private IGraphicsMesh? _mesh;
    private GraphicsMeshUploadCache? _uploads;
    private int _maximumRetainedBytes = 8 * 1024 * 1024;
    private bool _matchesUploaded, _snapshotPending, _editing, _disposed;
    private long _scopeId, _unchangedSkips;

    public GraphicsMeshUploadStream() { }
    public GraphicsMeshUploadStream(IGraphicsMesh mesh) => AttachMesh(mesh);

    public int Count => _count;
    public long retainedVertexBytes => (long)_vertices.Length * sizeof(float);
    public int retainedSnapshotBytes => _uploads?.retainedBytes ?? 0;
    public long uploadCount => _uploads?.uploadCount ?? 0;
    public long uploadedBytes => _uploads?.uploadedBytes ?? 0;
    public long skippedUploadCount => (_uploads?.skippedUploadCount ?? 0) + _unchangedSkips;
    /// <summary>Bounds the comparison snapshot, independently of the CPU vertex capacity.</summary>
    public int maximumRetainedBytes
    {
        get => _maximumRetainedBytes;
        set
        {
            ThrowIfUnavailable();
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (_maximumRetainedBytes == value) return;
            if (_uploads is not null) _uploads.maximumRetainedBytes = value;
            _maximumRetainedBytes = value;
            _snapshotPending = _matchesUploaded;
        }
    }

    /// <summary>Attaches one dynamic mesh, optionally after the first CPU write.</summary>
    public void AttachMesh(IGraphicsMesh mesh)
    {
        ThrowIfUnavailable();
        ArgumentNullException.ThrowIfNull(mesh);
        if (ReferenceEquals(_mesh, mesh)) return;
        if (_mesh is not null) throw new InvalidOperationException("The upload stream already owns a mesh binding.");
        var uploads = new GraphicsMeshUploadCache(mesh) { maximumRetainedBytes = _maximumRetainedBytes };
        _mesh = mesh; _uploads = uploads;
    }

    /// <summary>
    /// Starts a synchronous write. Retaining a snapshot permits comparison with
    /// the preceding upload; false avoids snapshot work for continuously changing
    /// streams. A failed producer write keeps its partial CPU changes but cannot
    /// establish a proof that those bytes were uploaded.
    /// </summary>
    public WriteScope BeginWrite(bool retainSnapshot = true)
    {
        ThrowIfUnavailable();
        if (retainSnapshot && _matchesUploaded && _snapshotPending)
        {
            _uploads!.CaptureUploadedSnapshot(_vertices.AsSpan(0, _count));
            _snapshotPending = false;
        }
        _scopeId = checked(_scopeId + 1);
        _editing = true;
        return new WriteScope(this, _scopeId);
    }

    /// <returns>True if an upload occurred. Unchanged CPU revisions need no comparison or upload.</returns>
    public bool Upload(bool retainSnapshot = true)
    {
        ThrowIfUnavailable();
        var uploads = _uploads ?? throw new InvalidOperationException("Attach a mesh before uploading.");
        if (_matchesUploaded) { _unchangedSkips++; return false; }
        _snapshotPending = false;
        var changed = true;
        if (retainSnapshot) changed = uploads.Update(_vertices.AsSpan(0, _count));
        else uploads.UpdateWithoutSnapshot(_vertices.AsSpan(0, _count));
        _matchesUploaded = true;
        _snapshotPending = !retainSnapshot;
        return changed;
    }

    /// <summary>Call after external mesh updates; forces the next Upload to restore this stream.</summary>
    public void Invalidate()
    {
        ThrowIfUnavailable();
        _uploads?.Invalidate(); _matchesUploaded = _snapshotPending = false;
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_editing) throw new InvalidOperationException("Complete the active vertex write first.");
    }

    private void ValidateWrite(long id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_editing || id != _scopeId) throw new InvalidOperationException("The vertex write has completed.");
    }

    private void EnsureCapacity(int count)
    {
        if (_vertices.Length >= count) return;
        // Match List<float>'s growth used by existing producers: start at the
        // requested range, then double. A fixed larger minimum would retain
        // substantially more memory for repeated 36/48-float quad writes.
        var capacity = (int)Math.Min(Array.MaxLength, Math.Max((long)count, Math.Max(4L, (long)_vertices.Length * 2)));
        Array.Resize(ref _vertices, capacity);
    }

    private Span<float> GetSpan(long id, int offset, int length)
    {
        ValidateWrite(id);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var end = checked(offset + length);
        EnsureCapacity(end);
        if (length == 0 && end <= _count) return _vertices.AsSpan(offset, 0);
        _matchesUploaded = _snapshotPending = false;
        if (offset > _count) _vertices.AsSpan(_count, offset - _count).Clear();
        _count = Math.Max(_count, end);
        return _vertices.AsSpan(offset, length);
    }

    private void SetLength(long id, int count)
    {
        ValidateWrite(id);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == _count) return;
        EnsureCapacity(count);
        _matchesUploaded = _snapshotPending = false;
        if (count > _count) _vertices.AsSpan(_count, count - _count).Clear();
        _count = count;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _editing = false;
        _uploads?.Dispose(); _uploads = null; _mesh = null;
        _vertices = []; _count = 0; _matchesUploaded = _snapshotPending = false;
    }

    public readonly ref struct WriteScope
    {
        private readonly GraphicsMeshUploadStream? _owner;
        private readonly long _id;
        internal WriteScope(GraphicsMeshUploadStream owner, long id) { _owner = owner; _id = id; }
        /// <summary>
        /// Borrows a writable range and extends Count if needed. Initialize every
        /// element of a newly exposed range; contents of that range are unspecified.
        /// A preceding gap is zero-filled. Any previous borrowed span expires.
        /// </summary>
        public Span<float> GetSpan(int offset, int length) =>
            (_owner ?? throw new InvalidOperationException("Uninitialized vertex write.")).GetSpan(_id, offset, length);
        /// <summary>Sets the active float count; newly exposed elements are zero-filled.</summary>
        public void SetLength(int count) =>
            (_owner ?? throw new InvalidOperationException("Uninitialized vertex write.")).SetLength(_id, count);
        public void Dispose()
        {
            // Copies of a completed scope cannot close a later write.
            if (_owner is not null && _owner._editing && _owner._scopeId == _id) _owner._editing = false;
        }
    }
}
