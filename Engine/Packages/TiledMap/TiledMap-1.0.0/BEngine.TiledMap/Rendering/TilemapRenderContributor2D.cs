using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.TiledMap;

internal sealed class TilemapRenderContributor2D(IGraphicsDevice device) : ISceneRenderContributor2D
{
    private readonly TilemapGpuRenderer _renderer = new(device);
    private IReadOnlyList<TilemapRenderer> _renderers = Array.Empty<TilemapRenderer>();
    private readonly List<TileCell> _candidates = [];
    private readonly Dictionary<TilePalette, Dictionary<int, TileDefinition>> _paletteDefinitions = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<Dictionary<int, TileDefinition>> _definitionPool = [];
    // Keep immutable boxed values, rather than mutating payloads borrowed by
    // earlier cameras/submissions. The bounded cache is populated on demand.
    private const int MaxCachedPayloads = 4096;
    private readonly List<object> _payloadCache = [];
    private int _payloadCount;

    public string packageId => "com.bengine.tiledmap";
    public bool processesQuads => false;

    public void BeginFrame() => _payloadCount = 0;

    public long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder)
        => Collect(scene, camera, width, height, submissions, submissionOrder, null);

    public long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder,
        Predicate<GameObject>? objectFilter)
    {
        var context = new SceneRenderContext2D(scene, camera, width, height, HierarchyOrder2D.Build(scene), objectFilter);
        return Collect(in context, submissions, submissionOrder);
    }

    public long Collect(in SceneRenderContext2D context, ICollection<RenderSubmission2D> submissions, long submissionOrder)
    {
        var scene = context.Scene; var camera = context.Camera;
        var width = context.Width; var height = context.Height;
        // Reuse the scene's cached read-only snapshot instead of copying it into a
        // per-frame list before the tilemap traversal.
        _renderers = scene.QueryComponents<TilemapRenderer>();
        using var rendererBudget = LoopExecutionGuard.AllowIterations(_renderers.Count + 1);
        Span<Vector2> boundary = stackalloc Vector2[4];
        for (var rendererIndex = 0; rendererIndex < _renderers.Count; rendererIndex++)
        {
            var renderer = _renderers[rendererIndex];
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || !camera.ContainsLayer(renderer.sortingLayer) ||
                context.ObjectFilter is not null && !context.ObjectFilter(renderer.gameObject)) continue;
            var tilemap = renderer.GetComponent<Tilemap>();
            if (tilemap is null || !tilemap.enabled || !tilemap.TryGetPalette(out var palette)) continue;
            if (!_paletteDefinitions.TryGetValue(palette, out var definitions))
            {
                definitions = _definitionPool.TryPop(out var reusable) ? reusable : [];
                foreach (var definition in palette.Tiles) definitions.Add(definition.Id, definition);
                _paletteDefinitions.Add(palette, definitions);
            }
            IReadOnlyList<TileCell> cells;
            if (renderer.cullOutsideCamera && tilemap.transform.lossyScale.x != 0 && tilemap.transform.lossyScale.y != 0)
            {
                // Match the old conservative per-tile culling radius for both 0 and 90 degree tile rotations.
                var a = tilemap.transform.TransformVector(new(tilemap.cellSize.x, 0));
                var b = tilemap.transform.TransformVector(new(0, tilemap.cellSize.y));
                var c = tilemap.transform.TransformVector(new(0, tilemap.cellSize.x));
                var d = tilemap.transform.TransformVector(new(tilemap.cellSize.y, 0));
                static Fix64 Extent(Vector2 x, Vector2 y) => (Fix64.Abs(x.x) + Fix64.Abs(x.y) + Fix64.Abs(y.x) + Fix64.Abs(y.y)) * Fix64.Half;
                camera.FillViewBoundary(boundary, width, height, Fix64.Max(Extent(a, b), Extent(c, d)));
                var min = tilemap.transform.InverseTransformPoint(boundary[0]); var max = min;
                for (var i = 1; i < boundary.Length; i++)
                {
                    var point = tilemap.transform.InverseTransformPoint(boundary[i]);
                    min = new(Fix64.Min(min.x, point.x), Fix64.Min(min.y, point.y));
                    max = new(Fix64.Max(max.x, point.x), Fix64.Max(max.y, point.y));
                }
                tilemap.FillRenderCandidates(new(min.x, min.y, max.x - min.x, max.y - min.y), renderer.sortOrder, _candidates);
                cells = _candidates;
            }
            else cells = tilemap.GetTiles(renderer.sortOrder);
            using var tileBudget = LoopExecutionGuard.AllowIterations(cells.Count + 1);
            using var cellEnumerator = new TileCellEnumerator(cells);
            while (cellEnumerator.MoveNext())
            {
                var cell = cellEnumerator.Current;
                var definition = TileVisualResolver.Resolve(tilemap, cell.Position, definitions, Time.time);
                if (definition is null) continue;
                var center = tilemap.CellToWorld(cell.Position);
                var rotation = Rotation(cell.Transform);
                var axisX = renderer.transform.TransformVector(TilemapMath2D.Rotate(new(tilemap.cellSize.x, 0), rotation));
                var axisY = renderer.transform.TransformVector(TilemapMath2D.Rotate(new(0, tilemap.cellSize.y), rotation));
                var size = new Vector2(Fix64.Abs(axisX.x) + Fix64.Abs(axisY.x), Fix64.Abs(axisX.y) + Fix64.Abs(axisY.y));
                if (renderer.cullOutsideCamera && !IsVisible(center, size, camera, width, height)) continue;
                var color = definition.tint * renderer.color;
                color = new Color(color.r, color.g, color.b, color.a * renderer.opacity);
                var texture = string.IsNullOrWhiteSpace(definition.Texture)
                    ? palette.Atlas
                    : definition.Texture;
                submissions.Add(new RenderSubmission2D(
                    new RenderSortKey2D(renderer.sortingLayer, renderer.orderInLayer,
                        context.GetHierarchyOrder(renderer.gameObject),
                        color.a < Fix64.One ? RenderTransparency.Transparent : RenderTransparency.Opaque,
                        submissionOrder++),
                    renderer.BatchKey(texture),
                    CachePayload(new TilemapRenderPayload(center, axisX, axisY, color, definition.uv,
                        (cell.Transform & TileTransformFlags.FlipX) != 0,
                        (cell.Transform & TileTransformFlags.FlipY) != 0, texture, tilemap.cellLayout, tilemap.hexSideRatio))));
            }
        }
        return submissionOrder;
    }

    public bool CanRender(RenderBatch2D batch) => batch.HasOnlyPayloads<TilemapRenderPayload>();

    public void Render(RenderBatch2D batch, RenderCamera camera, int width, int height, GraphicsRect viewport) =>
        _renderer.Render(batch.SubmissionSpan, camera, width, height, viewport);

    public void EndFrame()
    {
        if (_payloadCount < _payloadCache.Count)
            _payloadCache.RemoveRange(_payloadCount, _payloadCache.Count - _payloadCount);
        _payloadCount = 0;
        _renderers = Array.Empty<TilemapRenderer>(); _candidates.Clear();
        foreach (var definitions in _paletteDefinitions.Values)
        {
            definitions.Clear();
            if (_definitionPool.Count < 16) _definitionPool.Push(definitions);
        }
        _paletteDefinitions.Clear();
    }
    public void Dispose() { EndFrame(); _payloadCache.Clear(); _renderer.Dispose(); }

    private object CachePayload(TilemapRenderPayload value)
    {
        if (_payloadCount == MaxCachedPayloads) return value;
        var index = _payloadCount++;
        if (index < _payloadCache.Count)
        {
            var previous = _payloadCache[index];
            if ((TilemapRenderPayload)previous == value) return previous;
            return _payloadCache[index] = value;
        }
        object boxed = value;
        _payloadCache.Add(boxed);
        return boxed;
    }

    // Culling supplies the concrete reusable List; GetTiles supplies an owned
    // array. Preserve the List enumerator's fail-fast behavior without boxing.
    private struct TileCellEnumerator(IReadOnlyList<TileCell> cells) : IDisposable
    {
        private readonly bool _isList = cells is List<TileCell>;
        private List<TileCell>.Enumerator _list = cells is List<TileCell> list ? list.GetEnumerator() : default;
        private int _index = -1;
        internal TileCell Current => _isList ? _list.Current : cells[_index];
        internal bool MoveNext() => _isList ? _list.MoveNext() : ++_index < cells.Count;
        public void Dispose() => _list.Dispose();
    }

    private static Fix64 Rotation(TileTransformFlags flags)
    {
        var rotation = Fix64.Zero;
        if ((flags & TileTransformFlags.Rotate90) != 0) rotation += 90;
        if ((flags & TileTransformFlags.Rotate180) != 0) rotation += 180;
        return rotation;
    }

    private static bool IsVisible(Vector2 center, Vector2 size, RenderCamera camera, int width, int height)
    {
        var relative = TilemapMath2D.Rotate(center - camera.Position, -camera.Rotation);
        var radius = (size.x + size.y) * Fix64.Half;
        var halfHeight = camera.Size + radius;
        var halfWidth = camera.Size * Math.Max(1, width) / Math.Max(1, height) + radius;
        return Mathf.Abs(relative.x) <= halfWidth && Mathf.Abs(relative.y) <= halfHeight;
    }
}
