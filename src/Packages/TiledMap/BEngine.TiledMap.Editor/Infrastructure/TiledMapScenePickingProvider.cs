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
        foreach (var renderer in scene.QueryComponents<TilemapRenderer>().ToArray()
                     .Where(item => item.enabled && item.gameObject.activeInHierarchy)
                     .Where(item => objectFilter is null || objectFilter(item.gameObject))
                     .Where(item => camera.ContainsLayer(item.sortingLayer))
                     .OrderBy(item => hierarchy.GetValueOrDefault(item.gameObject)))
        {
            var tilemap = renderer.GetComponent<Tilemap>();
            if (tilemap is null || !tilemap.enabled || !tilemap.TryGetPalette(out var palette)) continue;
            var size = Abs(Vector2.Scale(tilemap.cellSize, renderer.transform.lossyScale));
            foreach (var cell in tilemap.GetTiles(renderer.sortOrder))
            {
                var definition = palette.Find(cell.TileId);
                if (definition is null) continue;
                var center = tilemap.CellToWorld(cell.Position);
                var rotation = renderer.transform.rotation + Rotation(cell.Transform);
                var color = definition.tint * renderer.color;
                color = new Color(color.r, color.g, color.b, color.a * renderer.opacity);
                var sortKey = new RenderSortKey2D(
                    renderer.sortingLayer,
                    renderer.orderInLayer,
                    hierarchy.GetValueOrDefault(renderer.gameObject),
                    color.a < Fix64.One ? RenderTransparency.Transparent : RenderTransparency.Opaque,
                    submissionOrder++);
                if (color.a <= Fix64.Zero ||
                    !ScenePickingUtility.ContainsQuad(viewportPoint, center, rotation, size,
                        camera, viewportWidth, viewportHeight)) continue;
                candidates.Add(new ScenePickCandidate(renderer.gameObject, sortKey));
            }
        }
        return submissionOrder;
    }

    private static Fix64 Rotation(TileTransformFlags flags)
    {
        var rotation = Fix64.Zero;
        if (flags.HasFlag(TileTransformFlags.Rotate90)) rotation += 90;
        if (flags.HasFlag(TileTransformFlags.Rotate180)) rotation += 180;
        return rotation;
    }

    private static Vector2 Abs(Vector2 value) =>
        new(Fix64.Abs(value.x), Fix64.Abs(value.y));
}
