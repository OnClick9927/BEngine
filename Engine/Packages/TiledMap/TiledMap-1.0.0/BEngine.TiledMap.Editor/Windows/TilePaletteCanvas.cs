using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.TiledMap.Editor;

public sealed partial class TilePaletteWindow
{
    private TileCoordinate _gestureStart, _gestureEnd, _sceneHover;
    private bool _gesture, _paletteGesture, _eraseGesture, _hover;
    private TilePaletteBrushMode _gestureMode;
    private int _gestureControl;
    private bool _palettePanning;
    private static readonly Color SelectionColor = new((Fix64).3, (Fix64).7, (Fix64)1, (Fix64).9);
    [OnOpenAsset]
    private static bool OpenPalette(string path)
    {
        if (!path.EndsWith(".tilepalette.yaml", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(EditorApplication.projectPath)) return false;
        var assetPath = EditorServices.GetRequiredService<PathUtility>()
            .GetRelativePath(EditorApplication.projectPath, path).Replace('\\', '/');
        if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
            !assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) return false;
        if (AssetDatabase.LoadAssetAtPath<TilePalette>(assetPath) is not { } palette) return false;
        var window = GetWindow<TilePaletteWindow>("Tile Palette"); window.controller.SelectPalette(palette); window.Show(); return true;
    }
    protected override void OnEnable()
    {
        SceneView.duringSceneGui += DrawSceneTools;
        SceneView.toolInteractionCancelled += CancelGesture;
        Undo.undoRedoPerformed += UndoChanged;
        OnSelectionChange();
    }
    protected override void OnDisable()
    {
        SceneView.duringSceneGui -= DrawSceneTools;
        SceneView.toolInteractionCancelled -= CancelGesture;
        Undo.undoRedoPerformed -= UndoChanged;
        CancelGesture();
        if (model.ScenePaintingEnabled && Tools.current == Tool.None) Tools.current = Tool.Move;
    }
    protected override void OnLostFocus() { if (_paletteGesture || _palettePanning) CancelGesture(); }
    private void UndoChanged() { CancelGesture(); Repaint(); SceneView.RepaintAll(); }
    private void CancelGesture()
    {
        if (_gestureControl != 0 && GUIUtility.hotControl == _gestureControl) GUIUtility.hotControl = 0;
        _gesture = false; _paletteGesture = false; _palettePanning = false; _gestureControl = 0;
        controller.EndStroke();
    }
    private void HandleShortcuts(bool scene = false)
    {
        var evt = Event.current;
        if (evt.type != EventType.KeyDown || EditorGUIUtility.editingTextField || evt.control || evt.command || evt.alt) return;
        TilePaletteBrushMode? mode = evt.keyCode switch {
            KeyCode.S => TilePaletteBrushMode.Select, KeyCode.M => TilePaletteBrushMode.Move,
            KeyCode.B => TilePaletteBrushMode.Paint, KeyCode.U => TilePaletteBrushMode.BoxFill,
            KeyCode.I => TilePaletteBrushMode.Pick, KeyCode.D => TilePaletteBrushMode.Erase,
            KeyCode.G => TilePaletteBrushMode.Flood, _ => null };
        if (mode is { } selected) { CancelGesture(); controller.SelectTool(selected); evt.Use(); }
        else if (evt.keyCode is KeyCode.LeftBracket or KeyCode.RightBracket)
        {
            if (evt.shift) controller.FlipBrush(evt.keyCode == KeyCode.LeftBracket);
            else controller.RotateBrush(evt.keyCode == KeyCode.RightBracket);
            evt.Use();
        }
        else if (evt.keyCode == KeyCode.Escape)
        { CancelGesture(); model.ScenePaintingEnabled = false; Tools.current = Tool.Move; evt.Use(); }
        else if ((scene || model.EditPalette) && evt.keyCode is KeyCode.Delete or KeyCode.Backspace)
        {
            TryEdit(() => controller.DeleteSelection(scene ? model.ActiveTilemap : model.EditPalette ? null : model.ActiveTilemap)); evt.Use();
        }
    }
    private void DrawPaletteCanvas(Rect area)
    {
        GUI.DrawRect(area, new Color((Fix64).105, (Fix64).115, (Fix64).13));
        GUI.BeginClip(area);
        try
        {
            var size = model.CellPixels;
            var origin = new Vector2(area.x + 24 + model.PaletteScroll.x, area.y + 24 + model.PaletteScroll.y);
            TileCoordinate ToCell(Vector2 point) => new((int)Math.Floor((double)((point.x - origin.x) / size)),
                -(int)Math.Floor((double)((point.y - origin.y) / size)));
            Rect CellRect(TileCoordinate cell) => new(origin.x + cell.X * size, origin.y - cell.Y * size, size, size);
            var min = ToCell(new Vector2(area.x, area.yMax)); var max = ToCell(new Vector2(area.xMax, area.y));
            for (var x = min.X; x <= max.X + 1; x++) GUI.DrawRect(new Rect(origin.x + x * size, area.y, 1, area.height), new Color((Fix64).18, (Fix64).2, (Fix64).23));
            for (var y = min.Y; y <= max.Y + 1; y++) GUI.DrawRect(new Rect(area.x, origin.y - y * size, area.width, 1), new Color((Fix64).18, (Fix64).2, (Fix64).23));
            var definitions = model.Palette.Tiles.ToDictionary(tile => tile.Id);
            using var budget = LoopExecutionGuard.AllowIterations(Math.Max(1, model.Palette.Tiles.Count + (model.Palette.Layout?.Count ?? 0) + 1));
            foreach (var cell in controller.GetPaletteCells())
            {
                var rect = CellRect(cell.Position);
                if (rect.xMax < area.x || rect.x > area.xMax || rect.yMax < area.y || rect.y > area.yMax) continue;
                if (definitions.TryGetValue(cell.TileId, out var definition)) DrawTile(Inset(rect, 1), model.Palette, definition, cell.Transform);
            }
            var selection = _gesture && _paletteGesture ? TilePaletteWindowController.Bounds(_gestureStart, _gestureEnd) : controller.PaletteSelectionBounds;
            if (!selection.IsEmpty)
            {
                var topLeft = CellRect(new TileCoordinate(selection.Min.X, selection.Max.Y));
                Outline(new Rect(topLeft.x, topLeft.y, (Fix64)((long)selection.Max.X - selection.Min.X + 1) * size,
                    (Fix64)((long)selection.Max.Y - selection.Min.Y + 1) * size), SelectionColor);
            }
            var evt = Event.current;
            if (area.Contains(evt.mousePosition) && evt.type == EventType.ScrollWheel)
            {
                var previous = model.CellPixels; model.CellPixels = Math.Clamp(previous - (int)evt.delta.y * 4, 16, 96);
                var mouse = evt.mousePosition - new Vector2(area.x + 24, area.y + 24);
                model.PaletteScroll = mouse - (mouse - model.PaletteScroll) * ((Fix64)model.CellPixels / previous); evt.Use(); Repaint();
            }
            if (evt.type == EventType.MouseDown && evt.button == 2 && area.Contains(evt.mousePosition))
            { _palettePanning = true; _gestureControl = GUIUtility.GetControlID(902381, FocusType.Passive, area); GUIUtility.hotControl = _gestureControl; evt.Use(); }
            if (_palettePanning && evt.type == EventType.MouseDrag)
            { var scroll = model.PaletteScroll + evt.delta; model.PaletteScroll = new Vector2(Fix64.Clamp(scroll.x, -1000000, 1000000), Fix64.Clamp(scroll.y, -1000000, 1000000)); evt.Use(); Repaint(); }
            if (_palettePanning && evt.type == EventType.MouseUp) { CancelGesture(); evt.Use(); }
            HandleGesture(null, ToCell(evt.mousePosition), area.Contains(evt.mousePosition), true, area);
        }
        finally { GUI.EndClip(); }
    }
    private void HandleGesture(Tilemap? map, TileCoordinate cell, bool inside, bool palette, Rect area)
    {
        var evt = Event.current;
        var id = GUIUtility.GetControlID(736812 + (palette ? 1 : 0), FocusType.Passive, area);
        if (evt.type == EventType.MouseDown && evt.button is 0 or 1 && inside && !evt.alt && GUIUtility.hotControl == 0)
        {
            _gesture = true; _paletteGesture = palette; _gestureStart = _gestureEnd = cell; _eraseGesture = evt.button == 1 || evt.shift;
            _gestureMode = palette && !model.EditPalette ? TilePaletteBrushMode.Pick : _eraseGesture ? TilePaletteBrushMode.Erase : model.BrushMode;
            _gestureControl = id; GUIUtility.hotControl = id; GUIUtility.keyboardControl = 0;
            if (palette) controller.BeginPaletteStroke(); else if (map is not null) controller.BeginStroke(map);
            if (_gestureMode is TilePaletteBrushMode.Paint or TilePaletteBrushMode.Erase or TilePaletteBrushMode.Flood) TryEdit(() => PaintGesture(map, cell));
            evt.Use(); Repaint(); SceneView.RepaintAll();
        }
        else if (_gesture && _paletteGesture == palette && GUIUtility.hotControl == id && evt.type == EventType.MouseDrag)
        {
            _gestureEnd = cell;
            if (_gestureMode is TilePaletteBrushMode.Paint or TilePaletteBrushMode.Erase) TryEdit(() => PaintGesture(map, cell));
            evt.Use(); Repaint(); SceneView.RepaintAll();
        }
        else if (_gesture && _paletteGesture == palette && GUIUtility.hotControl == id && evt.type == EventType.MouseUp)
        {
            _gestureEnd = cell;
            TryEdit(() => {
                switch (_gestureMode)
                {
                    case TilePaletteBrushMode.Pick:
                        if (palette) controller.PickPalette(_gestureStart, cell); else if (map is not null) controller.PickTiles(map, _gestureStart, cell);
                        break;
                    case TilePaletteBrushMode.Select: controller.SelectCells(_gestureStart, cell, palette); break;
                    case TilePaletteBrushMode.Move: controller.MoveSelection(map, cell - _gestureStart); break;
                    case TilePaletteBrushMode.BoxFill: controller.BoxFill(map, _gestureStart, cell, _eraseGesture); break;
                }
            });
            CancelGesture(); evt.Use(); Repaint(); SceneView.RepaintAll();
        }
    }
    private void PaintGesture(Tilemap? map, TileCoordinate cell)
    {
        if (map is not null) controller.PaintStroke(map, cell, _eraseGesture);
        else if (_gestureMode == TilePaletteBrushMode.Flood) controller.FloodPalette(cell);
        else controller.PaintPaletteStroke(cell, _eraseGesture);
    }
    private void TryEdit(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        { model.Message = exception.Message; CancelGesture(); Repaint(); }
    }
    private static Rect Inset(Rect rect, int inset) => new(rect.x + inset, rect.y + inset, Fix64.Max(1, rect.width - inset * 2), Fix64.Max(1, rect.height - inset * 2));
    private static void Outline(Rect rect, Color color)
    {
        GUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2), color); GUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), color);
        GUI.DrawRect(new Rect(rect.x, rect.y, 2, rect.height), color); GUI.DrawRect(new Rect(rect.xMax - 2, rect.y, 2, rect.height), color);
    }
    private static void DrawTile(Rect rect, TilePalette palette, TileDefinition tile, TileTransformFlags flags, Fix64 opacity = default)
    {
        var path = string.IsNullOrWhiteSpace(tile.Texture) ? palette.Atlas : tile.Texture;
        var tint = tile.tint; if (opacity > 0) tint = new Color(tint.r, tint.g, tint.b, tint.a * opacity);
        if (string.IsNullOrWhiteSpace(path)) { GUI.DrawRect(rect, tint); return; }
        var uv = tile.uv;
        if (flags.HasFlag(TileTransformFlags.FlipX)) uv = new Rect(uv.xMax, uv.y, -uv.width, uv.height);
        if (flags.HasFlag(TileTransformFlags.FlipY)) uv = new Rect(uv.x, uv.yMax, uv.width, -uv.height);
        GUI.DrawTextureWithTexCoords(rect, ResolvePath(path), uv, tint,
            (flags.HasFlag(TileTransformFlags.Rotate90) ? 1 : 0) + (flags.HasFlag(TileTransformFlags.Rotate180) ? 2 : 0));
    }
}
