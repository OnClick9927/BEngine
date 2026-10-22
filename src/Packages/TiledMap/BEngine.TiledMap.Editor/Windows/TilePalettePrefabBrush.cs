using BEngine.Editor;

namespace BEngine.TiledMap.Editor;

public sealed partial class TilePaletteWindowController
{
    private Dictionary<TileCoordinate, (PrefabAsset Prefab, TileTransformFlags Flags)> _prefabStamp = [];
    private readonly Dictionary<TileCoordinate, PrefabAsset> _prefabTargets = [];
    public void SetPrefabBrush(PrefabAsset? prefab)
    {
        EndStroke(); Model.PrefabBrush = prefab;
        _prefabStamp.Clear();
        if (prefab is not null)
        {
            _brush = [new(TileCoordinate.zero, 1)]; _brushWidth = _brushHeight = 1;
            Model.BrushMode = TilePaletteBrushMode.Paint;
            Model.ScenePaintingEnabled = true; Tools.current = Tool.None;
        }
        else ResetBrush();
        Repaint(); SceneView.RepaintAll();
    }
    private static IEnumerable<TilemapPrefabInstance> PrefabMarkers(Tilemap map) => map.transform.children
        .Select(child => child.gameObject.GetComponent<TilemapPrefabInstance>()).OfType<TilemapPrefabInstance>();
    public void PickPrefabs(Tilemap map, TileCoordinate first, TileCoordinate last)
    {
        var bounds = Bounds(first, last); ValidateArea(bounds, MaximumBrushCells);
        var markers = PrefabMarkers(map).Where(value => bounds.Contains(new(value.cellX, value.cellY)) && value.source is { isDestroyed: false }).ToArray();
        if (markers.Length == 0) return;
        SetPrefabBrush(markers[0].source);
        _prefabStamp = markers.ToDictionary(value => new TileCoordinate(value.cellX, value.cellY) - bounds.Min, value => (value.source!, value.transformFlags));
        _brushWidth = bounds.Width; _brushHeight = bounds.Height;
        _brush = Positions(bounds).Select(position => new TileCell(position - bounds.Min, _prefabStamp.ContainsKey(position - bounds.Min) ? 1 : 0,
            _prefabStamp.GetValueOrDefault(position - bounds.Min).Flags)).ToList();
        Model.BrushMode = TilePaletteBrushMode.Paint; Repaint();
    }
    public void MovePrefabs(Tilemap map, TileCoordinate offset)
    {
        if (!ReferenceEquals(_stroke, map) || SelectionBounds.IsEmpty || offset == TileCoordinate.zero) return;
        var selected = PrefabMarkers(map).Where(value => SelectionBounds.Contains(new(value.cellX, value.cellY))).ToArray();
        var targets = selected.ToDictionary(value => value, value => new TileCoordinate(checked(value.cellX + offset.X), checked(value.cellY + offset.Y)));
        var moved = new TileBounds(new(checked(SelectionBounds.Min.X + offset.X), checked(SelectionBounds.Min.Y + offset.Y)),
            new(checked(SelectionBounds.Max.X + offset.X), checked(SelectionBounds.Max.Y + offset.Y)));
        RecordStroke(map);
        var positions = targets.Values.ToHashSet();
        foreach (var occupied in PrefabMarkers(map).Where(value => !selected.Contains(value) && positions.Contains(new(value.cellX, value.cellY))).ToArray())
            Undo.DestroyObjectImmediate(occupied.gameObject);
        foreach (var (marker, coordinate) in targets)
        {
            Undo.RecordObject(marker, "Move Prefab Cells"); Undo.RecordObject(marker.transform, "Move Prefab Cells");
            marker.cellX = coordinate.X; marker.cellY = coordinate.Y; marker.transform.localPosition = map.CellToLocal(coordinate);
        }
        SelectionBounds = moved; MarkMapChanged(map);
    }
    public void FloodPrefabs(Tilemap map, TileCoordinate start)
    {
        if (!ReferenceEquals(_stroke, map) || Model.PrefabBrush is null) return;
        var markers = PrefabMarkers(map).ToDictionary(value => new TileCoordinate(value.cellX, value.cellY));
        if (markers.Count == 0) return;
        var bounds = new TileBounds(new(markers.Keys.Min(value => value.X), markers.Keys.Min(value => value.Y)),
            new(markers.Keys.Max(value => value.X), markers.Keys.Max(value => value.Y)));
        var source = markers.GetValueOrDefault(start)?.source;
        var pending = new Queue<TileCoordinate>(); var seen = new HashSet<TileCoordinate>(); var cells = new List<TileCell>(); pending.Enqueue(start);
        using var budget = LoopExecutionGuard.AllowIterations(MaximumEditCells * 6 + 1);
        while (pending.TryDequeue(out var position))
        {
            if (!bounds.Contains(position) || !seen.Add(position) || markers.GetValueOrDefault(position)?.source != source) continue;
            if (cells.Count >= MaximumEditCells) throw new InvalidOperationException("Prefab fill exceeds 65536 cells.");
            cells.Add(new(position, 1, Model.Flags)); foreach (var neighbor in map.GetNeighbors(position)) pending.Enqueue(neighbor);
        }
        ApplyPrefabCells(map, cells, false);
    }
    public void RelayoutPrefabs(Tilemap map)
    {
        if (!EditorAssetWritePolicy.CanWrite) return;
        Undo.IncrementCurrentGroup();
        foreach (var marker in PrefabMarkers(map))
        {
            var position = map.CellToLocal(new(marker.cellX, marker.cellY));
            if (marker.transform.localPosition == position) continue;
            Undo.RecordObject(marker.transform, "Relayout Prefab Cells"); marker.transform.localPosition = position;
        }
        MarkMapChanged(map);
    }

    private void ApplyPrefabCells(Tilemap map, IReadOnlyList<TileCell> cells, bool erase)
    {
        if (!ReferenceEquals(_stroke, map) || Model.PrefabBrush is not { isDestroyed: false } prefab || map.gameObject.scene is not { } scene) return;
        if (!EditorAssetWritePolicy.CanWrite) return;
        var existing = map.transform.children.Select(child => child.gameObject.GetComponent<TilemapPrefabInstance>())
            .Where(marker => marker is not null).Cast<TilemapPrefabInstance>()
            .ToLookup(marker => new TileCoordinate(marker.cellX, marker.cellY));
        using var budget = LoopExecutionGuard.AllowIterations(cells.Count + 1);
        foreach (var cell in cells)
        {
            if (!erase && cell.TileId == 0) continue;
            var selectedPrefab = prefab;
            if (_prefabTargets.TryGetValue(cell.Position, out var picked)) selectedPrefab = picked;
            var current = existing[cell.Position].Where(marker => !marker.isDestroyed).ToArray();
            if (erase && current.Length == 0) continue;
            if (!erase && current.Length == 1 && ReferenceEquals(current[0].source, selectedPrefab) &&
                current[0].transformFlags == cell.Transform && current[0].transform.localPosition == map.CellToLocal(cell.Position)) continue;
            RecordStroke(map);
            if (erase) { foreach (var marker in current) Undo.DestroyObjectImmediate(marker.gameObject); continue; }
            var instance = selectedPrefab.Instantiate(scene, map.transform);
            instance.transform.localPosition = map.CellToLocal(cell.Position);
            instance.transform.localRotation += (cell.Transform.HasFlag(TileTransformFlags.Rotate90) ? 90 : 0) +
                (cell.Transform.HasFlag(TileTransformFlags.Rotate180) ? 180 : 0);
            instance.transform.localScale = Vector2.Scale(instance.transform.localScale, new Vector2(
                cell.Transform.HasFlag(TileTransformFlags.FlipX) ? -1 : 1, cell.Transform.HasFlag(TileTransformFlags.FlipY) ? -1 : 1));
            var markerComponent = instance.GetComponent<TilemapPrefabInstance>() ?? instance.AddComponent<TilemapPrefabInstance>();
            markerComponent.cellX = cell.Position.X; markerComponent.cellY = cell.Position.Y; markerComponent.source = selectedPrefab;
            markerComponent.transformFlags = cell.Transform;
            Undo.RegisterCreatedObjectUndo(instance, "Paint Prefab Brush");
            foreach (var marker in current) Undo.DestroyObjectImmediate(marker.gameObject);
        }
        MarkMapChanged(map);
    }
}
