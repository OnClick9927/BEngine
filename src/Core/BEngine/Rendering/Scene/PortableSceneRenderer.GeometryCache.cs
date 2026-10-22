using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Quad = BEngine.Rendering.SceneQuad2D;

namespace BEngine.Rendering;

public sealed partial class PortableSceneRenderer
{
    private readonly record struct GeometryKey(Vector2 Center, Fix64 Rotation, Vector2 Size,
        Vector2? AxisX, Vector2? AxisY, Color A, Color B, Color C, Color D,
        Rect Uv, bool FlipX, bool FlipY, bool Textured, int Offset)
    {
        internal static GeometryKey From(Quad quad, bool textured, int offset) => new(
            quad.Center, quad.Rotation, quad.Size, quad.WorldAxisX, quad.WorldAxisY,
            quad.ColorA, quad.ColorB, quad.ColorC, quad.ColorD, quad.Uv, quad.FlipX, quad.FlipY, textured, offset);
    }
    private GeometryKey[] _geometryKeys = [];
    private int _geometryKeyCount;
    private int _geometryCacheBudget = 8 * 1024 * 1024;
    private RenderViewportTransform2D _geometryProjection;
    private bool _geometryProjectionKnown;
    private bool _geometryCacheValid;
    private bool _triangleSnapshotPending, _texturedSnapshotPending;
    private readonly List<float> _coreTriangleVertices = [];
    private long _geometryBuiltQuads, _geometryReusedQuads;

    /// <summary>Maximum retained quad keys. Zero disables reuse; vertex buffers retain their existing capacity.</summary>
    public int geometryCacheBudgetBytes
    {
        get => _geometryCacheBudget;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (_geometryCacheBudget == value) return;
            _geometryCacheBudget = value; ClearGeometryCache();
        }
    }
    public long geometryCacheRetainedBytes => (long)_geometryKeys.Length * Unsafe.SizeOf<GeometryKey>();
    /// <summary>Invalidates CPU geometry reuse without changing scene objects or package-owned meshes.</summary>
    public void ClearGeometryCache()
    {
        _geometryKeys = []; _geometryKeyCount = 0; _geometryCacheValid = false; _geometryProjectionKnown = false;
    }

    private void PrepareCachedBatchGeometry(int batchCount, RenderCamera camera, int width, int height)
    {
        _batchContributors.Clear(); _coreBatchRanges.Clear();
        var projection = camera.CreateViewportTransform(width, height);
        var sameProjection = _geometryProjectionKnown && projection.Equals(_geometryProjection);
        var rebuiltProjection = _geometryProjectionKnown && !sameProjection;
        var captureTriangles = !rebuiltProjection && _triangleSnapshotPending;
        var captureTextures = !rebuiltProjection && _texturedSnapshotPending;
        // A failed rebuild may partially overwrite either CPU stream. Clear
        // these proofs before any capture/write; only successful uploads renew
        // them. ClearGeometryCache itself leaves the stream bytes untouched.
        _triangleSnapshotPending = _texturedSnapshotPending = false;
        if (captureTriangles)
            _triangleUploads.CaptureUploadedSnapshot(CollectionsMarshal.AsSpan(_coreTriangleVertices));
        if (captureTextures)
            _texturedUploads!.CaptureUploadedSnapshot(CollectionsMarshal.AsSpan(_texturedVertices));
        var collectKeys = !_geometryProjectionKnown || sameProjection;
        var reuse = _geometryCacheValid && sameProjection;
        // Any exception must invalidate partially rewritten keys and vertices.
        _geometryCacheValid = false;
        var limit = _geometryCacheBudget / Unsafe.SizeOf<GeometryKey>();
        var keyCount = 0;
        var triangleCount = 0; var texturedCount = 0;
        var trianglesChanged = false; var texturesChanged = false;
        for (var index = 0; index < batchCount; index++)
        {
            var batch = _batches[index];
            ISceneRenderContributor2D? handler = null;
            foreach (var contributor in _frameContributors)
                if (contributor.CanRender(batch)) { handler = contributor; break; }
            _batchContributors.Add(handler);
            if (handler is not null) { _coreBatchRanges.Add(default); continue; }
            var first = default(Quad); var found = false;
            foreach (ref readonly var submission in batch.SubmissionSpan)
                if (TryGetQuad(in submission, out first)) { found = true; break; }
            if (!found) { _coreBatchRanges.Add(default); continue; }
            var textured = first.IsTextured;
            var vertices = textured ? _texturedVertices : _coreTriangleVertices;
            var stride = textured ? 8 : 6;
            var firstOffset = textured ? texturedCount : triangleCount;
            var offset = firstOffset;
            // Only the built-in, unmasked solid shader has a known output alpha.
            // Texture alpha and contributor-defined blending remain unchanged.
            var opaque = !textured && batch.BatchKey.MaskGroup == 0 && opaqueSolidBlendingEnabled;
            foreach (ref readonly var submission in batch.SubmissionSpan)
            {
                if (!TryGetQuad(in submission, out var quad)) continue;
                opaque &= HasOpaqueCorners(quad);
                // Residency demand is frame-sensitive even when geometry is unchanged.
                if (textured && TextureStreaming.enabled && quad.Uv.width != 0 && quad.Uv.height != 0 && camera.Size > 0)
                    TextureStreaming.ReportProjectedSize(quad.Texture, (float)(Fix64.Max(Fix64.Abs(quad.Size.x / quad.Uv.width), Fix64.Abs(quad.Size.y / quad.Uv.height)) * height / (camera.Size * 2)));
                // A moving projection already requires every vertex to be rebuilt.
                // Defer the large key copy until the camera settles; the following
                // stable frame can reuse it without changing any rendered geometry.
                var retainKey = collectKeys && keyCount < limit;
                var key = retainKey ? GeometryKey.From(quad, textured, offset) : default;
                var unchanged = reuse && retainKey && keyCount < _geometryKeyCount && _geometryKeys[keyCount] == key;
                if (unchanged) _geometryReusedQuads++;
                else
                {
                    if (textured) WriteTexturedQuadAt(vertices, offset, quad, projection);
                    else WriteQuadAt(vertices, offset, quad, projection);
                    if (textured) texturesChanged = true; else trianglesChanged = true;
                    _geometryBuiltQuads++;
                }
                if (retainKey)
                {
                    if (keyCount == _geometryKeys.Length)
                        Array.Resize(ref _geometryKeys, Math.Min(limit, Math.Max(16, _geometryKeys.Length * 2)));
                    _geometryKeys[keyCount] = key;
                }
                keyCount++; offset = checked(offset + stride * 6);
            }
            if (textured) texturedCount = offset; else triangleCount = offset;
            _coreBatchRanges.Add(new CoreBatchRange(firstOffset / stride, (offset - firstOffset) / stride,
                textured, first.Texture, opaque));
        }
        trianglesChanged |= _coreTriangleVertices.Count != triangleCount;
        texturesChanged |= _texturedVertices.Count != texturedCount;
        CollectionsMarshal.SetCount(_coreTriangleVertices, triangleCount);
        CollectionsMarshal.SetCount(_texturedVertices, texturedCount);
        if (triangleCount > 0 && trianglesChanged)
        {
            Upload(_triangleUploads, _coreTriangleVertices, rebuiltProjection);
            _triangleSnapshotPending = rebuiltProjection;
        }
        if (texturedCount > 0 && texturesChanged)
        {
            EnsureTexturedPipeline();
            _texturedUploads ??= new Rhi.GraphicsMeshUploadCache(_texturedMesh!);
            Upload(_texturedUploads, _texturedVertices, rebuiltProjection);
            _texturedSnapshotPending = rebuiltProjection;
        }
        _geometryProjection = projection;
        _geometryProjectionKnown = true;
        _geometryKeyCount = collectKeys ? Math.Min(keyCount, limit) : 0;
        _geometryCacheValid = collectKeys && keyCount <= limit;
    }
}
