using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.TiledMap;

internal sealed class TilemapRenderContributor2D(IGraphicsDevice device) : ISceneRenderContributor2D
{
    private readonly TilemapGpuRenderer _renderer = new(device);

    public string packageId => "com.bengine.tiledmap";

    public long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder)
        => Collect(scene, camera, width, height, submissions, submissionOrder, null);

    public long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder,
        Predicate<GameObject>? objectFilter)
    {
        var hierarchy = HierarchyOrder2D.Build(scene);
        foreach (var renderer in scene.QueryComponents<TilemapRenderer>().ToArray()
                     .Where(item => item.enabled && item.gameObject.activeInHierarchy)
                     .Where(item => objectFilter is null || objectFilter(item.gameObject))
                     .OrderBy(item => hierarchy.GetValueOrDefault(item.gameObject)))
        {
            var tilemap = renderer.GetComponent<Tilemap>();
            if (tilemap is null || !tilemap.enabled || !tilemap.TryGetPalette(out var palette)) continue;
            var lossyScale = renderer.transform.lossyScale;
            var size = new Vector2(
                Mathf.Abs(tilemap.cellSize.x * lossyScale.x),
                Mathf.Abs(tilemap.cellSize.y * lossyScale.y));
            foreach (var cell in tilemap.GetTiles(renderer.sortOrder))
            {
                var definition = palette.Find(cell.TileId);
                if (definition is null) continue;
                var center = tilemap.CellToWorld(cell.Position);
                if (renderer.cullOutsideCamera && !IsVisible(center, size, camera, width, height)) continue;
                var color = definition.tint * renderer.color;
                color = new Color(color.r, color.g, color.b, color.a * renderer.opacity);
                var texture = string.IsNullOrWhiteSpace(definition.Texture)
                    ? palette.Atlas
                    : definition.Texture;
                submissions.Add(new RenderSubmission2D(
                    new RenderSortKey2D(renderer.sortingLayer, renderer.orderInLayer,
                        hierarchy.GetValueOrDefault(renderer.gameObject),
                        color.a < Fix64.One ? RenderTransparency.Transparent : RenderTransparency.Opaque,
                        submissionOrder++),
                    renderer.BatchKey(texture),
                    new TilemapRenderPayload(center,
                        renderer.transform.rotation + Rotation(cell.Transform),
                        size, color, definition.uv,
                        cell.Transform.HasFlag(TileTransformFlags.FlipX),
                        cell.Transform.HasFlag(TileTransformFlags.FlipY), texture)));
            }
        }
        return submissionOrder;
    }

    public bool CanRender(RenderBatch2D batch) => batch.Submissions.Count > 0 &&
        batch.Submissions.All(submission => submission.Payload is TilemapRenderPayload);

    public void Render(RenderBatch2D batch, RenderCamera camera, int width, int height, GraphicsRect viewport) =>
        _renderer.Render(batch.Submissions.Select(submission => (TilemapRenderPayload)submission.Payload),
            camera, width, height, viewport);

    public void Dispose() => _renderer.Dispose();

    private static Fix64 Rotation(TileTransformFlags flags)
    {
        var rotation = Fix64.Zero;
        if (flags.HasFlag(TileTransformFlags.Rotate90)) rotation += 90;
        if (flags.HasFlag(TileTransformFlags.Rotate180)) rotation += 180;
        return rotation;
    }

    private static bool IsVisible(Vector2 center, Vector2 size, RenderCamera camera, int width, int height)
    {
        var relative = TilemapMath2D.Rotate(center - camera.Position, -camera.Rotation);
        var halfHeight = camera.Size + size.y;
        var halfWidth = camera.Size * Math.Max(1, width) / Math.Max(1, height) + size.x;
        return Mathf.Abs(relative.x) <= halfWidth && Mathf.Abs(relative.y) <= halfHeight;
    }
}
