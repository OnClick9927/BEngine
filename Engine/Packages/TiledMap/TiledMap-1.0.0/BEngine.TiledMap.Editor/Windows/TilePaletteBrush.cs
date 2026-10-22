using BEngine.Editor;

namespace BEngine.TiledMap.Editor;

public sealed partial class TilePaletteWindowController
{
    public const int MaximumBrushCells = 4096;
    public const int MaximumEditCells = 65536;
    private List<TileCell> _brush = [new(TileCoordinate.zero, 1)];
    private int _brushWidth = 1, _brushHeight = 1;
    private Tilemap? _stroke;
    private TileCoordinate? _last;
    private bool _recorded, _paletteStroke;
    public int BrushWidth => _brushWidth;
    public int BrushHeight => _brushHeight;
    public IReadOnlyList<TileCell> Brush => _brush;
    public TileBounds SelectionBounds { get; private set; } = TileBounds.Empty;
    public TileBounds PaletteSelectionBounds { get; private set; } = TileBounds.Empty;

    public void ResetBrush()
    {
        Model.SelectedTileId = Model.Palette.Tiles.FirstOrDefault()?.Id ?? 0; Model.Flags = TileTransformFlags.None;
        _brush = [new(TileCoordinate.zero, Model.SelectedTileId)]; _brushWidth = _brushHeight = 1;
        PaletteSelectionBounds = TileBounds.Empty;
    }
    public IReadOnlyList<TileCell> GetPaletteCells()
    {
        if (Model.Palette.Layout is { } layout)
            return layout.Select(cell => new TileCell(new TileCoordinate(cell.X, cell.Y), cell.TileId, cell.Transform)).ToArray();
        return Model.Palette.Tiles.Select((tile, index) => new TileCell(new TileCoordinate(index % Math.Max(1, Model.Palette.Columns),
            -index / Math.Max(1, Model.Palette.Columns)), tile.Id)).ToArray();
    }
    public void PickPalette(TileCoordinate start, TileCoordinate end)
    {
        var bounds = Bounds(start, end); ValidateArea(bounds, MaximumBrushCells);
        PickCells(GetPaletteCells(), bounds); PaletteSelectionBounds = bounds;
        if (Model.BrushMode == TilePaletteBrushMode.Pick) Model.BrushMode = TilePaletteBrushMode.Paint;
        Model.ScenePaintingEnabled = true; Tools.current = Tool.None; Repaint(); SceneView.RepaintAll();
    }
    public void PickTiles(Tilemap map, TileCoordinate start, TileCoordinate end)
    {
        if (Model.PrefabBrush is not null) { PickPrefabs(map, start, end); return; }
        var bounds = Bounds(start, end); ValidateArea(bounds, MaximumBrushCells);
        if (map.palette is { } palette && !CompatiblePalette(map)) SelectPalette(palette);
        PickCells(map.GetTiles(), bounds); Model.BrushMode = TilePaletteBrushMode.Paint; Repaint();
    }
    private void PickCells(IReadOnlyList<TileCell> source, TileBounds bounds)
    {
        var cells = source.Where(cell => bounds.Contains(cell.Position)).ToDictionary(cell => cell.Position);
        _brushWidth = bounds.Width; _brushHeight = bounds.Height; _brush = [];
        using var budget = LoopExecutionGuard.AllowIterations(MaximumBrushCells + 1);
        foreach (var position in Positions(bounds))
        {
            var cell = cells.GetValueOrDefault(position);
            _brush.Add(new TileCell(position - bounds.Min, cell.TileId, cell.Transform));
        }
        Model.SelectedTileId = _brush.FirstOrDefault(cell => cell.TileId > 0).TileId; Model.Flags = TileTransformFlags.None;
    }
    public void RotateBrush(bool clockwise)
    {
        _prefabStamp = _prefabStamp.ToDictionary(pair => clockwise ? new TileCoordinate(pair.Key.Y, _brushWidth - 1 - pair.Key.X)
            : new TileCoordinate(_brushHeight - 1 - pair.Key.Y, pair.Key.X), pair => (pair.Value.Prefab, RotateFlags(pair.Value.Flags, clockwise ? -1 : 1)));
        _brush = _brush.Select(cell => cell with {
            Position = clockwise ? new TileCoordinate(cell.Position.Y, _brushWidth - 1 - cell.Position.X) :
                new TileCoordinate(_brushHeight - 1 - cell.Position.Y, cell.Position.X),
            Transform = RotateFlags(cell.Transform, clockwise ? -1 : 1) }).ToList();
        (_brushWidth, _brushHeight) = (_brushHeight, _brushWidth); Repaint(); SceneView.RepaintAll();
    }
    public void FlipBrush(bool horizontal)
    {
        _prefabStamp = _prefabStamp.ToDictionary(pair => horizontal ? new TileCoordinate(_brushWidth - 1 - pair.Key.X, pair.Key.Y)
            : new TileCoordinate(pair.Key.X, _brushHeight - 1 - pair.Key.Y), pair => (pair.Value.Prefab, FlipFlags(pair.Value.Flags, horizontal)));
        _brush = _brush.Select(cell => cell with {
            Position = horizontal ? new TileCoordinate(_brushWidth - 1 - cell.Position.X, cell.Position.Y) :
                new TileCoordinate(cell.Position.X, _brushHeight - 1 - cell.Position.Y),
            Transform = FlipFlags(cell.Transform, horizontal) }).ToList();
        Repaint(); SceneView.RepaintAll();
    }
    private static TileTransformFlags RotateFlags(TileTransformFlags flags, int turn)
    {
        var rotation = ((flags.HasFlag(TileTransformFlags.Rotate90) ? 1 : 0) +
            (flags.HasFlag(TileTransformFlags.Rotate180) ? 2 : 0) + turn + 4) % 4;
        return (flags & (TileTransformFlags.FlipX | TileTransformFlags.FlipY)) |
            ((rotation & 1) != 0 ? TileTransformFlags.Rotate90 : 0) | ((rotation & 2) != 0 ? TileTransformFlags.Rotate180 : 0);
    }
    private static TileTransformFlags FlipFlags(TileTransformFlags flags, bool horizontal)
    {
        var rotation = (flags.HasFlag(TileTransformFlags.Rotate90) ? 1 : 0) + (flags.HasFlag(TileTransformFlags.Rotate180) ? 2 : 0);
        return RotateFlags(flags, -2 * rotation) ^ (horizontal ? TileTransformFlags.FlipX : TileTransformFlags.FlipY);
    }
    public void BeginStroke(Tilemap tilemap)
    {
        EndStroke();
        if (tilemap.isDestroyed || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (Model.PrefabBrush is null && Model.BrushMode is not (TilePaletteBrushMode.Pick or TilePaletteBrushMode.Select or TilePaletteBrushMode.Move or TilePaletteBrushMode.Erase) && !CompatiblePalette(tilemap))
        { Model.Message = "Assign this palette to the active Tilemap before painting."; return; }
        _stroke = tilemap;
    }
    public void BeginPaletteStroke() { EndStroke(); _paletteStroke = Model.EditPalette; }
    public void EndStroke() { _stroke = null; _last = null; _recorded = false; _paletteStroke = false; _prefabTargets.Clear(); }
    public void PaintStroke(Tilemap tilemap, TileCoordinate position, bool erase = false)
    {
        if (tilemap.isDestroyed || !ReferenceEquals(_stroke, tilemap)) return;
        if (Model.PrefabBrush is not null)
        {
            if (!erase && Model.BrushMode == TilePaletteBrushMode.Pick) { PickPrefabs(tilemap, position, position); return; }
            if (!erase && Model.BrushMode == TilePaletteBrushMode.Flood) { if (!_last.HasValue) FloodPrefabs(tilemap, position); _last = position; return; }
            ApplyPrefabCells(tilemap, StrokeCells(position, erase || Model.BrushMode == TilePaletteBrushMode.Erase), erase || Model.BrushMode == TilePaletteBrushMode.Erase);
            _last = position; return;
        }
        if (Model.BrushMode == TilePaletteBrushMode.Pick && !erase) { PickTiles(tilemap, position, position); return; }
        if (Model.BrushMode == TilePaletteBrushMode.Flood && !erase)
        {
            if (_last.HasValue) return;
            if (_brushWidth != 1 || _brushHeight != 1) { Model.Message = "Flood Fill requires a single tile brush."; return; }
            if (Model.SelectedTileId <= 0) return;
            RecordStroke(tilemap);
            tilemap.FloodFill(position, Model.SelectedTileId, _brush[0].Transform | Model.Flags, MaximumEditCells);
        }
        else ApplyCells(tilemap, StrokeCells(position, erase || Model.BrushMode == TilePaletteBrushMode.Erase));
        _last = position; MarkMapChanged(tilemap);
    }
    public void PaintPaletteStroke(TileCoordinate position, bool erase)
    {
        if (!_paletteStroke) return;
        ApplyPaletteCells(StrokeCells(position, erase || Model.BrushMode == TilePaletteBrushMode.Erase));
        _last = position;
    }
    private List<TileCell> StrokeCells(TileCoordinate end, bool erase)
    {
        var start = _last ?? end;
        var steps = Math.Max(Math.Abs((long)end.X - start.X), Math.Abs((long)end.Y - start.Y));
        if (steps > MaximumEditCells || (steps + 1) * _brush.Count > MaximumEditCells)
            throw new ArgumentException("This stroke is too large. Zoom in or use a smaller brush.");
        var cells = new Dictionary<TileCoordinate, TileCell>();
        using var budget = LoopExecutionGuard.AllowIterations(MaximumEditCells + 1);
        for (var i = 0; i <= steps; i++)
        {
            var point = steps == 0 ? end : new TileCoordinate(start.X + (int)Math.Round(((double)end.X - start.X) * i / steps),
                start.Y + (int)Math.Round(((double)end.Y - start.Y) * i / steps));
            foreach (var cell in Stamp(point, erase)) cells[cell.Position] = cell;
        }
        return cells.Values.ToList();
    }
    public IEnumerable<TileCell> Stamp(TileCoordinate position, bool erase = false)
    {
        using var budget = LoopExecutionGuard.AllowIterations(MaximumBrushCells + 1);
        foreach (var cell in _brush)
        {
            var target = new TileCoordinate(checked(position.X + cell.Position.X), checked(position.Y + cell.Position.Y));
            if (_prefabStamp.TryGetValue(cell.Position, out var picked)) _prefabTargets[target] = picked.Prefab;
            yield return new TileCell(target, erase ? 0 : cell.TileId, cell.Transform | Model.Flags);
        }
    }
    public void BoxFill(Tilemap? map, TileCoordinate start, TileCoordinate end, bool erase = false)
    {
        var bounds = Bounds(start, end); ValidateArea(bounds, MaximumEditCells);
        var brush = _brush.ToDictionary(cell => cell.Position);
        var cells = new List<TileCell>();
        using var budget = LoopExecutionGuard.AllowIterations(MaximumEditCells + 1);
        foreach (var position in Positions(bounds))
        {
            var local = new TileCoordinate((position.X - bounds.Min.X) % _brushWidth, (position.Y - bounds.Min.Y) % _brushHeight);
            var cell = brush.GetValueOrDefault(local);
            if (_prefabStamp.TryGetValue(local, out var picked)) _prefabTargets[position] = picked.Prefab;
            cells.Add(new TileCell(position, erase ? 0 : cell.TileId, cell.Transform | Model.Flags));
        }
        if (map is not null && Model.PrefabBrush is not null) ApplyPrefabCells(map, cells, erase);
        else if (map is null) ApplyPaletteCells(cells); else ApplyCells(map, cells);
    }
    public void FloodPalette(TileCoordinate start)
    {
        if (!_paletteStroke || _last.HasValue) return;
        if (_brushWidth != 1 || _brushHeight != 1 || Model.SelectedTileId <= 0)
        { Model.Message = "Flood Fill requires a single tile brush."; return; }
        var layout = GetPaletteCells().ToDictionary(cell => cell.Position);
        if (layout.Count == 0) return;
        var bounds = new TileBounds(new TileCoordinate(layout.Keys.Min(cell => cell.X), layout.Keys.Min(cell => cell.Y)),
            new TileCoordinate(layout.Keys.Max(cell => cell.X), layout.Keys.Max(cell => cell.Y)));
        if (!bounds.Contains(start)) return;
        var target = layout.GetValueOrDefault(start).TileId;
        var visited = new HashSet<TileCoordinate>(); var queue = new Queue<TileCoordinate>(); var cells = new List<TileCell>();
        queue.Enqueue(start);
        using var budget = LoopExecutionGuard.AllowIterations(MaximumEditCells * 4 + 1);
        while (queue.Count > 0)
        {
            var point = queue.Dequeue();
            if (!visited.Add(point) || !bounds.Contains(point) || layout.GetValueOrDefault(point).TileId != target) continue;
            if (cells.Count >= MaximumEditCells) throw new ArgumentException("Palette fill exceeds 65536 cells.");
            cells.Add(new TileCell(point, Model.SelectedTileId, _brush[0].Transform));
            if (point.X < bounds.Max.X) queue.Enqueue(new TileCoordinate(point.X + 1, point.Y));
            if (point.X > bounds.Min.X) queue.Enqueue(new TileCoordinate(point.X - 1, point.Y));
            if (point.Y < bounds.Max.Y) queue.Enqueue(new TileCoordinate(point.X, point.Y + 1));
            if (point.Y > bounds.Min.Y) queue.Enqueue(new TileCoordinate(point.X, point.Y - 1));
        }
        ApplyPaletteCells(cells); _last = start;
    }
    public void SelectCells(TileCoordinate start, TileCoordinate end, bool palette = false)
    {
        var bounds = Bounds(start, end); ValidateArea(bounds, MaximumEditCells);
        if (palette) PaletteSelectionBounds = bounds; else SelectionBounds = bounds; Repaint(); SceneView.RepaintAll();
    }
    public void MoveSelection(Tilemap? map, TileCoordinate offset)
    {
        if (map is not null && Model.PrefabBrush is not null) { MovePrefabs(map, offset); return; }
        using var budget = LoopExecutionGuard.AllowIterations(MaximumEditCells + 1);
        var bounds = map is null ? PaletteSelectionBounds : SelectionBounds;
        if (bounds.IsEmpty || offset == TileCoordinate.zero) return;
        ValidateArea(bounds, MaximumEditCells / 2);
        var source = (map is null ? GetPaletteCells() : map.GetTiles()).Where(cell => bounds.Contains(cell.Position)).ToArray();
        var cells = Positions(bounds).Select(position => new TileCell(position, 0)).ToList();
        foreach (var cell in source) cells.Add(cell with { Position = new TileCoordinate(checked(cell.Position.X + offset.X), checked(cell.Position.Y + offset.Y)) });
        var moved = new TileBounds(new TileCoordinate(checked(bounds.Min.X + offset.X), checked(bounds.Min.Y + offset.Y)),
            new TileCoordinate(checked(bounds.Max.X + offset.X), checked(bounds.Max.Y + offset.Y)));
        if (map is null) { ApplyPaletteCells(cells); PaletteSelectionBounds = moved; }
        else { ApplyCells(map, cells); SelectionBounds = moved; }
    }
    public void DeleteSelection(Tilemap? map)
    {
        var bounds = map is null ? PaletteSelectionBounds : SelectionBounds;
        if (bounds.IsEmpty) return;
        ValidateArea(bounds, MaximumEditCells);
        var cells = Positions(bounds).Select(position => new TileCell(position, 0)).ToArray();
        if (map is null) { BeginPaletteStroke(); ApplyPaletteCells(cells); }
        else { BeginStroke(map); if (Model.PrefabBrush is not null) ApplyPrefabCells(map, cells, true); else ApplyCells(map, cells); }
        EndStroke();
    }
    public void ApplyBrush(Tilemap tilemap, TileCoordinate position) { BeginStroke(tilemap); PaintStroke(tilemap, position); EndStroke(); }
    private void ApplyCells(Tilemap map, IReadOnlyList<TileCell> cells)
    {
        if (!ReferenceEquals(map, _stroke) || cells.Count == 0) return;
        RecordStroke(map); map.SetTiles(cells); MarkMapChanged(map);
    }
    private void ApplyPaletteCells(IReadOnlyList<TileCell> cells)
    {
        if (!_paletteStroke || cells.Count == 0) return;
        var layout = GetPaletteCells().ToDictionary(cell => cell.Position);
        using var budget = LoopExecutionGuard.AllowIterations(MaximumEditCells + 1);
        foreach (var cell in cells) { if (cell.TileId == 0) layout.Remove(cell.Position); else layout[cell.Position] = cell; }
        if (layout.Count > MaximumEditCells) throw new ArgumentException("Palette layout is limited to 65536 cells.");
        if (!_recorded) { Undo.IncrementCurrentGroup(); Undo.RecordObject(Model.Palette, "Edit Tile Palette Layout"); _recorded = true; }
        Model.Palette.Layout = layout.Values.Select(cell => new TilePaletteCell { X = cell.Position.X, Y = cell.Position.Y,
            TileId = cell.TileId, Transform = cell.Transform }).ToList();
        EditorUtility.SetDirty(Model.Palette); Repaint();
    }
    private void RecordStroke(Tilemap tilemap)
    {
        if (_recorded) return;
        Undo.IncrementCurrentGroup(); Undo.RecordObject(tilemap, $"{Model.BrushMode} Tilemap"); _recorded = true;
    }
    private static void MarkMapChanged(Tilemap map) { EditorUtility.SetDirty(map); SceneView.RepaintAll(); }
    public static TileBounds Bounds(TileCoordinate first, TileCoordinate second) => new(
        new TileCoordinate(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y)),
        new TileCoordinate(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y)));
    public static void ValidateArea(TileBounds bounds, int limit)
    {
        var width = (long)bounds.Max.X - bounds.Min.X + 1; var height = (long)bounds.Max.Y - bounds.Min.Y + 1;
        if (width < 1 || height < 1 || width > limit || height > limit || width * height > limit)
            throw new ArgumentException($"The selected area exceeds {limit} cells.");
    }
    private static IEnumerable<TileCoordinate> Positions(TileBounds bounds)
    {
        using var budget = LoopExecutionGuard.AllowIterations(MaximumEditCells + 1);
        for (long y = bounds.Min.Y; y <= bounds.Max.Y; y++)
        for (long x = bounds.Min.X; x <= bounds.Max.X; x++) yield return new TileCoordinate((int)x, (int)y);
    }
}
