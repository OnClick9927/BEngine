using BEngine.Editor;

namespace BEngine.TiledMap.Editor;

public sealed partial class TilePaletteWindow
{
    private void DrawSceneTools(SceneViewToolContext context)
    {
        if (!model.ScenePaintingEnabled || Tools.current != Tool.None || EditorApplication.isPlayingOrWillChangePlaymode) { if (_gesture && !_paletteGesture) CancelGesture(); return; }
        HandleShortcuts(true);
        if (model.ActiveTilemap is not { isDestroyed: false } map || map.gameObject.scene is null) { if (_gesture) CancelGesture(); return; }
        if (SceneVisibilityManager.instance.IsHidden(map.gameObject)) { model.Message = "Show the active Tilemap to paint it."; return; }
        if (map.transform.lossyScale.x == 0 || map.transform.lossyScale.y == 0 || map.cellSize.x + map.cellGap.x <= 0 || map.cellSize.y + map.cellGap.y <= 0)
        { model.Message = "Tilemap scale and cell stride must be non-zero."; return; }
        context.suppressDefaultTools = true;
        _hover = context.pointerInViewport;
        try { _sceneHover = map.WorldToCell(context.GUIToWorld(Event.current.mousePosition)); }
        catch (OverflowException) { return; }
        HandleGesture(map, _sceneHover, _hover, false, context.viewport);
        if (Event.current.type != EventType.Repaint) return;
        try { DrawSceneGrid(context, map); } catch (OverflowException) { return; }
        DrawSceneSelection(context, map, controller.SelectionBounds, SelectionColor);
        if (_gesture && !_paletteGesture && _gestureMode is TilePaletteBrushMode.BoxFill or TilePaletteBrushMode.Select or TilePaletteBrushMode.Pick)
            DrawSceneSelection(context, map, TilePaletteWindowController.Bounds(_gestureStart, _gestureEnd), SelectionColor);
        else if (_gesture && !_paletteGesture && _gestureMode == TilePaletteBrushMode.Move && !controller.SelectionBounds.IsEmpty)
        {
            var delta = _gestureEnd - _gestureStart;
            var bounds = controller.SelectionBounds;
            TryEdit(() => DrawSceneSelection(context, map, new TileBounds(
                new TileCoordinate(checked(bounds.Min.X + delta.X), checked(bounds.Min.Y + delta.Y)),
                new TileCoordinate(checked(bounds.Max.X + delta.X), checked(bounds.Max.Y + delta.Y))), SelectionColor));
        }
        else if (_hover && model.BrushMode is TilePaletteBrushMode.Paint or TilePaletteBrushMode.Erase or TilePaletteBrushMode.Flood)
        {
            var erase = model.BrushMode == TilePaletteBrushMode.Erase || Event.current.shift;
            var color = erase ? new Color(1, (Fix64).3, (Fix64).3, (Fix64).9) : SelectionColor;
            TryEdit(() => {
                var bounds = new TileBounds(_sceneHover, new TileCoordinate(checked(_sceneHover.X + controller.BrushWidth - 1), checked(_sceneHover.Y + controller.BrushHeight - 1)));
                DrawSceneSelection(context, map, bounds, color);
                // Preview pixels for unrotated maps; the transformed cell outline is exact for any map transform.
                if (!erase && model.PrefabBrush is null && map.cellLayout == TilemapLayout.Rectangle && controller.Brush.Count <= 64 && map.transform.rotation == 0)
                    foreach (var cell in controller.Stamp(_sceneHover))
                    {
                        if (model.Palette.Find(cell.TileId) is not { } tile) continue;
                        var bottomLeft = context.WorldToGUI(CellCorner(map, cell.Position.X, cell.Position.Y));
                        var topRight = context.WorldToGUI(CellCorner(map, (long)cell.Position.X + 1, (long)cell.Position.Y + 1));
                        DrawTile(new Rect(Fix64.Min(bottomLeft.x, topRight.x), Fix64.Min(bottomLeft.y, topRight.y),
                            Fix64.Abs(topRight.x - bottomLeft.x), Fix64.Abs(topRight.y - bottomLeft.y)), model.Palette, tile, cell.Transform, (Fix64).45);
                    }
            });
        }
        var label = $"Tile Palette | {model.BrushMode} | {map.gameObject.name} | Cell {_sceneHover.X}, {_sceneHover.Y} | Esc: exit";
        var rect = new Rect(context.viewport.x + 8, context.viewport.y + 8, Fix64.Min(670, context.viewport.width - 16), 24);
        GUI.DrawRect(rect, new Color((Fix64).10, (Fix64).12, (Fix64).15, (Fix64).95));
        GUI.Label(rect, label, EditorStyles.miniLabel);
    }
    private static Vector2 CellCorner(Tilemap map, long x, long y) => map.transform.TransformPoint(new Vector2(
        (Fix64)x * (map.cellSize.x + map.cellGap.x), (Fix64)y * (map.cellSize.y + map.cellGap.y)));
    private static void DrawSceneSelection(SceneViewToolContext context, Tilemap map, TileBounds bounds, Color color)
    {
        if (bounds.IsEmpty) return;
        if (map.cellLayout != TilemapLayout.Rectangle)
        {
            DrawCellOutlines(context, map, bounds.Min.X, bounds.Max.X, bounds.Min.Y, bounds.Max.Y, color, 2);
            return;
        }
        var first = CellCorner(map, bounds.Min.X, bounds.Min.Y);
        var second = CellCorner(map, (long)bounds.Max.X + 1, bounds.Min.Y);
        var third = CellCorner(map, (long)bounds.Max.X + 1, (long)bounds.Max.Y + 1);
        var fourth = CellCorner(map, bounds.Min.X, (long)bounds.Max.Y + 1);
        context.DrawWorldLine(first, second, color, 2); context.DrawWorldLine(second, third, color, 2);
        context.DrawWorldLine(third, fourth, color, 2); context.DrawWorldLine(fourth, first, color, 2);
    }
    private static void DrawSceneGrid(SceneViewToolContext context, Tilemap map)
    {
        var rect = context.viewport;
        TileCoordinate[] corners;
        try { corners = new[] {
            map.WorldToCell(context.GUIToWorld(new Vector2(rect.x, rect.y))), map.WorldToCell(context.GUIToWorld(new Vector2(rect.xMax, rect.y))),
            map.WorldToCell(context.GUIToWorld(new Vector2(rect.xMax, rect.yMax))), map.WorldToCell(context.GUIToWorld(new Vector2(rect.x, rect.yMax))) }; }
        catch (OverflowException) { return; }
        long minX = corners.Min(cell => cell.X), maxX = (long)corners.Max(cell => cell.X) + 1;
        long minY = corners.Min(cell => cell.Y), maxY = (long)corners.Max(cell => cell.Y) + 1;
        if (map.cellLayout != TilemapLayout.Rectangle)
        {
            DrawCellOutlines(context, map, minX - 1, maxX + 1, minY - 1, maxY + 1,
                new Color((Fix64).35, (Fix64).45, (Fix64).52, (Fix64).28), 1);
            return;
        }
        var stride = Math.Max(1, Math.Max(maxX - minX, maxY - minY) / 100 + 1);
        var color = new Color((Fix64).35, (Fix64).45, (Fix64).52, (Fix64).28);
        for (var x = minX; x <= maxX; x += stride)
            context.DrawWorldLine(CellCorner(map, x, minY), CellCorner(map, x, maxY), color, 1);
        for (var y = minY; y <= maxY; y += stride)
            context.DrawWorldLine(CellCorner(map, minX, y), CellCorner(map, maxX, y), color, 1);
    }

    private static void DrawCellOutlines(SceneViewToolContext context, Tilemap map,
        long minX, long maxX, long minY, long maxY, Color color, int width)
    {
        var step = Math.Max(1L, (long)Math.Ceiling(Math.Sqrt((double)(maxX - minX + 1) * (maxY - minY + 1) / 1600)));
        Span<Vector2> points = stackalloc Vector2[6];
        for (var y = Math.Max(int.MinValue, minY); y <= Math.Min(int.MaxValue, maxY); y += step)
        for (var x = Math.Max(int.MinValue, minX); x <= Math.Min(int.MaxValue, maxX); x += step)
        {
            var count = map.GetCellLocalOutline(new((int)x, (int)y), points);
            for (var index = 0; index < count; index++)
                context.DrawWorldLine(map.transform.TransformPoint(points[index]),
                    map.transform.TransformPoint(points[(index + 1) % count]), color, width);
        }
    }
}
