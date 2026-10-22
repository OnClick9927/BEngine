using System.Runtime.CompilerServices;
using BEngine.Editor;
using BEngine.Rendering;

namespace BEngine.TiledMap.Editor;

internal static class TiledMapScenePickingProvider
{
    private const string ProviderId = "com.bengine.tiledmap";

    [ModuleInitializer]
    internal static void Register() =>
        ScenePickingProviderRegistry.Register(ProviderId, Collect);

    private static long Collect(
        Scene scene,
        RenderCamera camera,
        Vector2 viewportPoint,
        int viewportWidth,
        int viewportHeight,
        ICollection<ScenePickCandidate> candidates,
        long submissionOrder,
        Predicate<GameObject>? objectFilter)
    {
        var hierarchy = HierarchyOrder2D.Build(scene);
        using var rendererBudget = LoopExecutionGuard.AllowIterations(scene.QueryComponents<TilemapRenderer>().Count() + 1);
        foreach (var renderer in scene.QueryComponents<TilemapRenderer>().ToArray()
                     .Where(item => item.enabled && item.gameObject.activeInHierarchy)
                     .Where(item => objectFilter is null || objectFilter(item.gameObject))
                     .Where(item => camera.ContainsLayer(item.sortingLayer))
                     .OrderBy(item => hierarchy.GetValueOrDefault(item.gameObject)))
        {
            var tilemap = renderer.GetComponent<Tilemap>();
            if (tilemap is null || !tilemap.enabled || !tilemap.TryGetPalette(out var palette)) continue;
            var definitions = palette.Tiles.ToDictionary(tile => tile.Id);
            using var tileBudget = LoopExecutionGuard.AllowIterations(tilemap.cellCount + 1);
            foreach (var cell in tilemap.GetTiles(renderer.sortOrder))
            {
                var definition = TileVisualResolver.Resolve(tilemap, cell.Position, definitions, Time.time);
                if (definition is null) continue;
                var color = definition.tint * renderer.color;
                color = new Color(color.r, color.g, color.b, color.a * renderer.opacity);
                var sortKey = new RenderSortKey2D(
                    renderer.sortingLayer,
                    renderer.orderInLayer,
                    hierarchy.GetValueOrDefault(renderer.gameObject),
                    color.a < Fix64.One ? RenderTransparency.Transparent : RenderTransparency.Opaque,
                    submissionOrder++);
                if (color.a <= Fix64.Zero ||
                    !ContainsCell(viewportPoint, tilemap, cell,
                        camera, viewportWidth, viewportHeight)) continue;
                candidates.Add(new ScenePickCandidate(renderer.gameObject, sortKey));
            }
        }
        return submissionOrder;
    }

    private static bool ContainsCell(Vector2 point, Tilemap map, TileCell cell, RenderCamera camera, int width, int height)
    {
        if (map.cellSize.x <= 0 || map.cellSize.y <= 0 || map.transform.lossyScale.x == 0 || map.transform.lossyScale.y == 0) return false;
        var pixelsPerUnit = (Fix64)camera.PixelsPerUnit(height);
        var viewportCenter = new Vector2(camera.AlignViewportCenter ? width / 2 : (Fix64)width / 2,
            camera.AlignViewportCenter ? height / 2 : (Fix64)height / 2);
        var world = camera.CameraToWorld(new Vector2((point.x - viewportCenter.x) / pixelsPerUnit,
            (viewportCenter.y - point.y) / pixelsPerUnit));
        var center = map.CellToWorld(cell.Position);
        var relative = map.transform.InverseTransformPoint(world - (camera.SnapWorldPoint(center) - center)) - map.CellToLocal(cell.Position);
        var rotation = Rotation(cell.Transform);
        var sine = Mathf.Sin(-rotation * Mathf.Deg2Rad); var cosine = Mathf.Cos(-rotation * Mathf.Deg2Rad);
        var local = new Vector2(relative.x * cosine - relative.y * sine, relative.x * sine + relative.y * cosine);
        return TilemapGeometry.Contains(map.cellLayout, map.cellSize, local, map.hexSideRatio);
    }

    private static Fix64 Rotation(TileTransformFlags flags)
    {
        var rotation = Fix64.Zero;
        if (flags.HasFlag(TileTransformFlags.Rotate90)) rotation += 90;
        if (flags.HasFlag(TileTransformFlags.Rotate180)) rotation += 180;
        return rotation;
    }

}
