using System.Runtime.InteropServices;

namespace BEngine.Rendering;

// Private-owner batches can borrow their storage while retaining the original
// List mutation exception. Public arbitrary Lists continue through foreach.
internal interface ISceneOwnedQuadBatchSink2D
{
    void SubmitOwnedQuads(Renderer2D renderer, SpriteQuadBuffer2D quads, bool snapCenters);
}

/// <summary>A renderer-owned reusable sprite-source cache and ordered world-quad queue.</summary>
/// <remarks>Clear retains storage and resolved sources. This object is not shared between renderers.</remarks>
public sealed class SpriteQuadBuffer2D
{
    private readonly List<SceneQuadSubmission2D> _quads;
    private readonly Dictionary<Sprite, SpriteRenderData2D> _sources;
    private readonly Material _material;
    private int _version;

    public SpriteQuadBuffer2D(int initialQuadCapacity = 160, int initialSourceCapacity = 64,
        Material? material = null)
    {
        _quads = new(initialQuadCapacity);
        _sources = new(initialSourceCapacity);
        _material = material ?? new Material(Shader.Find("BEngine/Sprite"));
    }

    public int Count => _quads.Count;

    public void Clear()
    {
        unchecked { _version++; }
        _quads.Clear();
    }

    /// <summary>Resolves a source without enqueueing geometry.</summary>
    public void PrepareSprite(Sprite sprite)
    {
        if (!_sources.ContainsKey(sprite))
            _sources.Add(sprite, SpriteSourceRegistry.Resolve(sprite));
    }

    public void Add(Sprite sprite, Vector2 position, Vector2 size, Color color, Fix64 rotation = default)
    {
        if (!_sources.TryGetValue(sprite, out var source))
            _sources.Add(sprite, source = SpriteSourceRegistry.Resolve(sprite));
        // Preserve the existing unrotated pivot offset, signed size and fixed-point order.
        var center = position + new Vector2(
            (Fix64.Half - source.Pivot.x) * size.x,
            (Fix64.Half - source.Pivot.y) * size.y);
        var item = new SceneQuadSubmission2D(SceneQuad2D.Create(center, rotation, size, color, source.Texture,
            source.Uv, false, false, source.IsTextured), new RenderBatchKey2D(_material, source.BatchIdentity));
        // List.Add increments its version before growing/assigning storage.
        // Construct the complete entry first, matching argument evaluation.
        unchecked { _version++; }
        _quads.Add(item);
    }

    /// <summary>Preserves list mutation checks and the sink's per-item camera/submit callbacks.</summary>
    public void CollectGeometry(ISceneGeometrySink2D sink, Renderer2D renderer, bool snapCenters = true)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(renderer);
        if (sink is ISceneOwnedQuadBatchSink2D owned)
        {
            owned.SubmitOwnedQuads(renderer, this, snapCenters);
            return;
        }
        SceneGeometryBatch.SubmitQuads(sink, renderer, _quads, snapCenters);
    }

    internal OwnedCursor CreateCursor() => new(this);

    internal struct OwnedCursor
    {
        private readonly SpriteQuadBuffer2D _owner;
        private readonly int _version;
        private List<SceneQuadSubmission2D>.Enumerator _versionGuard;
        private int _index;

        internal OwnedCursor(SpriteQuadBuffer2D owner)
        {
            _owner = owner;
            _version = owner._version;
            _versionGuard = owner._quads.GetEnumerator();
            _index = 0;
        }

        internal bool MoveNext(out SceneQuad2D quad, out RenderBatchKey2D key)
        {
            // Both private mutators mirror List's unchecked version changes.
            // Delegate mismatch reporting to that exact List enumerator so
            // exception type, message and localization remain unchanged.
            if (_version != _owner._version) _versionGuard.MoveNext();
            var values = CollectionsMarshal.AsSpan(_owner._quads);
            if ((uint)_index < (uint)values.Length)
            {
                ref readonly var item = ref values[_index++];
                // Snapshot both fields before policy/contributor callbacks.
                quad = item.Quad;
                key = item.Key;
                return true;
            }
            quad = default;
            key = default;
            return false;
        }
    }
}
