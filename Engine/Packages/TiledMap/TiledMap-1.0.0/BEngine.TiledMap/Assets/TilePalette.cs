using BEngine.Serialization;

namespace BEngine.TiledMap;

[EditorIcon("Icons/Assets/AssetImage.png")]
public sealed class TilePalette : ScriptableObject
{
    public string Format { get; set; } = "BEngine.TilePalette";
    public int Version { get; set; } = 1;
    [YamlDotNet.Serialization.YamlIgnore, HideInInspector]
    public string Name { get => name; set => name = value; }
    public string Atlas { get; set; } = string.Empty;
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    public List<TileDefinition> Tiles { get; set; } = [];

    // Null preserves the automatic atlas layout of older palettes; an empty list is an intentionally empty palette.
    public List<TilePaletteCell>? Layout { get; set; }

    public TileDefinition? Find(int id) => Tiles.FirstOrDefault(tile => tile.Id == id);

    public void Validate()
    {
        if (!string.Equals(Format, "BEngine.TilePalette", StringComparison.Ordinal) || Version != 1)
            throw new InvalidDataException($"Unsupported tile palette format/version '{Format}' v{Version}.");
        if (string.IsNullOrWhiteSpace(Name)) Name = "Tile Palette";
        Columns = Math.Max(1, Columns);
        Rows = Math.Max(1, Rows);
        Tiles ??= [];
        if (Tiles.Count > 65536 || Tiles.Any(tile => tile is null)) throw new InvalidDataException("Invalid tile palette entries.");
        using var budget = LoopExecutionGuard.AllowIterations(Math.Max(Tiles.Count, Layout?.Count ?? 0) + 1);
        foreach (var tile in Tiles) tile.Validate();
        if (Layout is { } layout && (layout.Count > 65536 || layout.Any(cell => cell is null || cell.TileId < 0) ||
            layout.Select(cell => (cell.X, cell.Y)).Distinct().Count() != layout.Count))
            throw new InvalidDataException("Invalid tile palette layout.");
        var duplicate = Tiles.GroupBy(tile => tile.Id).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException($"Tile ID {duplicate.Key} is duplicated.");
        var ids = Tiles.Select(tile => tile.Id).ToHashSet();
        foreach (var tile in Tiles)
            if (tile.Rules.Any(rule => !ids.Contains(rule.OutputTileId)) || tile.Animation.Any(frame => !ids.Contains(frame.TileId)))
                throw new InvalidDataException($"Tile {tile.Id} references a missing rule output or animation frame.");
        Tiles = Tiles.OrderBy(tile => tile.Id).ToList();
    }

    public void SliceAtlas(int columns, int rows, int tileCount = -1)
    {
        Layout = null;
        Columns = Math.Max(1, columns);
        Rows = Math.Max(1, rows);
        var count = tileCount < 0 ? checked(Columns * Rows) : Math.Clamp(tileCount, 0, checked(Columns * Rows));
        if (count > 65536) throw new ArgumentOutOfRangeException(nameof(tileCount), "A palette is limited to 65536 tiles.");
        var previous = Tiles.ToDictionary(tile => tile.Id);
        var width = Fix64.One / Columns;
        var height = Fix64.One / Rows;
        Tiles = Enumerable.Range(0, count).Select(index =>
        {
            var id = index + 1;
            var tile = previous.GetValueOrDefault(id) ?? new TileDefinition { Id = id, Name = $"Tile {id}" };
            tile.UvX = index % Columns * width;
            tile.UvY = index / Columns * height;
            tile.UvWidth = width;
            tile.UvHeight = height;
            return tile;
        }).ToList();
    }

    public void ImportAtlas(TextureAtlas atlas)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        atlas.Validate();
        if (atlas.Sprites.Count == 0 || string.IsNullOrWhiteSpace(atlas.Texture))
            throw new InvalidDataException("Build the texture atlas before importing it into a tile palette.");
        var previousByName = Tiles.GroupBy(tile => tile.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        Layout = null;
        Atlas = atlas.Texture;
        Columns = 1;
        Rows = 1;
        Tiles = atlas.Sprites.OrderBy(sprite => sprite.Name, StringComparer.Ordinal)
            .Select((sprite, index) =>
            {
                var previous = previousByName.GetValueOrDefault(sprite.Name);
                var uv = sprite.NormalizedUv(atlas.Width, atlas.Height);
                return new TileDefinition
                {
                    Id = index + 1,
                    Name = sprite.Name,
                    UvX = uv.x,
                    UvY = uv.y,
                    UvWidth = uv.width,
                    UvHeight = uv.height,
                    Red = previous?.Red ?? byte.MaxValue,
                    Green = previous?.Green ?? byte.MaxValue,
                    Blue = previous?.Blue ?? byte.MaxValue,
                    Alpha = previous?.Alpha ?? byte.MaxValue,
                    HasCollider = previous?.HasCollider ?? false,
                    CollisionPaths = previous?.CollisionPaths?.Select(path => new TileCollisionPath2D
                    { Closed = path.Closed, IsHole = path.IsHole, Points = path.Points.ToList() }).ToList()
                };
            }).ToList();
    }

    public static TilePalette Load(string path)
    {
        var resolved = TilePalettePath.Resolve(path);
        var palette = RuntimeServiceAccess.FileSystem.FileExists(resolved)
            ? YamlUtility.Load<TilePalette>(resolved)
            : YamlUtility.Deserialize<TilePalette>((Resources.Load<TextAsset>(path) ??
                throw new FileNotFoundException("Tile palette is missing from active resources.", path)).text);
        palette.Validate();
        palette.BindAssetReference(path);
        return palette;
    }

    public void Save(string path)
    {
        EnsureAssetCanBeSaved();
        Validate();
        YamlUtility.Save(this, TilePalettePath.Resolve(path));
        BindAssetReference(path);
        BAsset.Invalidate(path);
    }
}

public sealed class TilePaletteCell
{
    public int X { get; set; }
    public int Y { get; set; }
    public int TileId { get; set; }
    public TileTransformFlags Transform { get; set; }
}
