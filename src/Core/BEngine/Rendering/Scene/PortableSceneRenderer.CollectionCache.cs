using System.Runtime.CompilerServices;
using Quad = BEngine.Rendering.SceneQuad2D;

namespace BEngine.Rendering;

public sealed partial class PortableSceneRenderer
{
    private readonly RenderSubmissionSortCache2D _submissionSort = new();
    private readonly RenderCullingContext2D _culling = new(RenderCamera.Default, 1, 1);
    private readonly GeometrySink _geometrySink;
    private SpriteGeometryEntry[] _spriteGeometry = [];
    private int _collectionBudget = 4 * 1024 * 1024;
    private CullingKey _lastCulling;
    private long _cullingVersion;
    private long _worldBuilt, _worldReused, _cullTests, _cullReused, _culledSprites, _fullSorts, _reusedSorts;

    // No references to scene objects, textures, effects or camera policy instances.
    private struct SpriteGeometryEntry
    {
        internal int RendererId, TransformId;
        internal long WorldVersion, CullingVersion;
        internal Vector2 Size, Pivot, Center, AxisX, AxisY, WorldSize;
        internal Fix64 Rotation;
        internal bool NegativeX, NegativeY, Visible;
    }
    private readonly record struct CullingKey(Vector2 Position, Fix64 Rotation, Fix64 Size, int Width, int Height);

    /// <summary>Retained simple-sprite world geometry and visibility keys; zero disables collection reuse.</summary>
    public int collectionCacheBudgetBytes
    {
        get => _collectionBudget;
        set { ObjectDisposedException.ThrowIf(_disposed, this); ArgumentOutOfRangeException.ThrowIfNegative(value); _collectionBudget = value; ClearCollectionCache(); }
    }
    public long collectionCacheRetainedBytes => (long)_spriteGeometry.Length * Unsafe.SizeOf<SpriteGeometryEntry>();
    /// <summary>Retained sort keys and integer permutations. Payloads are never retained.</summary>
    public int sortCacheBudgetBytes
    {
        get => _submissionSort.BudgetBytes;
        set { ObjectDisposedException.ThrowIf(_disposed, this); _submissionSort.BudgetBytes = value; }
    }
    public long sortCacheRetainedBytes => _submissionSort.RetainedBytes;
    public void ClearCollectionCache() { _spriteGeometry = []; _cullingVersion = 0; }
    public void ClearSortCache() => _submissionSort.Clear();

    private void PrepareCulling(RenderCamera camera, int width, int height)
    {
        _culling.Reset(camera, width, height);
        var key = new CullingKey(camera.Position, camera.Rotation, camera.Size, width, height);
        if (_cullingVersion == 0 || key != _lastCulling)
        {
            _lastCulling = key;
            // Rollover is practically unreachable, but must not reuse obsolete visibility keys.
            if (_cullingVersion == long.MaxValue) ClearCollectionCache();
            _cullingVersion++;
        }
    }

    private bool TryCollectSimpleSprite(SpriteRenderer renderer, SpriteRenderData2D visual,
        Vector2 pivot, Color color, RenderCamera camera, int slot, out Quad quad)
    {
        var transform = renderer.transform;
        var version = transform.worldVersion;
        var limit = _collectionBudget / Unsafe.SizeOf<SpriteGeometryEntry>();
        var cached = slot < limit;
        if (cached && slot >= _spriteGeometry.Length)
            Array.Resize(ref _spriteGeometry, Math.Min(limit, Math.Max(slot + 1, Math.Max(16, _spriteGeometry.Length * 2))));
        var entry = cached ? _spriteGeometry[slot] : default;
        var same = cached && entry.RendererId == renderer.GetInstanceID() && entry.TransformId == transform.GetInstanceID() &&
            entry.WorldVersion == version && entry.Size == renderer.size && entry.Pivot == pivot;
        if (same) _worldReused++;
        else
        {
            transform.GetWorldPoseUnchecked(out _, out var rotation, out var scale);
            var localCenter = new Vector2((Fix64.Half - pivot.x) * renderer.size.x, (Fix64.Half - pivot.y) * renderer.size.y);
            var matrix = transform.localToWorldMatrix;
            var size = Abs(renderer.size); var renderedSize = Vector2.Scale(renderer.size, scale);
            var x = matrix.TransformVector(new(size.x * Fix64.Half, 0));
            var y = matrix.TransformVector(new(0, size.y * Fix64.Half));
            if (scale.x < 0) x = -x;
            if (scale.y < 0) y = -y;
            entry = new SpriteGeometryEntry
            {
                RendererId = renderer.GetInstanceID(), TransformId = transform.GetInstanceID(), WorldVersion = version,
                Size = renderer.size, Pivot = pivot, Center = matrix.TransformPoint(localCenter), AxisX = x, AxisY = y,
                Rotation = rotation, WorldSize = Abs(renderedSize), NegativeX = renderedSize.x < 0, NegativeY = renderedSize.y < 0
            };
            _worldBuilt++;
        }
        var reuseVisibility = same && camera.Policy is null && entry.CullingVersion == _cullingVersion;
        if (reuseVisibility)
        {
            _cullReused++;
            // Skip even the large Quad value construction/copy for an unchanged offscreen sprite.
            if (!entry.Visible) { _culledSprites++; quad = default; return false; }
        }
        // Policy callbacks can mutate in place or independently snap each object; run them every query.
        var center = camera.SnapWorldPoint(entry.Center);
        quad = CreateQuad(center, entry.Rotation, entry.WorldSize, color, visual.Texture, visual.Uv,
            renderer.flipX ^ entry.NegativeX, renderer.flipY ^ entry.NegativeY, visual.IsTextured)
            with { WorldAxisX = entry.AxisX, WorldAxisY = entry.AxisY };
        if (!reuseVisibility)
        {
            entry.Visible = _culling.IsVisible(quad); _cullTests++;
            // Do not allow a previous custom-policy result to leak into an ordinary camera.
            entry.CullingVersion = camera.Policy is null ? _cullingVersion : 0;
        }
        if (cached && !reuseVisibility) _spriteGeometry[slot] = entry;
        if (!entry.Visible) _culledSprites++;
        return entry.Visible;
    }
}
