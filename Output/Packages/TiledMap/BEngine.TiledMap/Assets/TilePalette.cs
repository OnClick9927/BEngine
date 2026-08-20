using BEngine.Serialization;

namespace BEngine.TiledMap;

public sealed class TilePalette
{
    public string Format { get; set; } = "BEngine.TilePalette";
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "New Tile Palette";
    public string Atlas { get; set; } = string.Empty;
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    public List<TileDefinition> Tiles { get; set; } = [];

    public TileDefinition? Find(int id) => Tiles.FirstOrDefault(tile => tile.Id == id);

    public void Validate()
    {
        if (!Format.Equals("BEngine.TilePalette", StringComparison.Ordinal) || Version != 1)
            throw new InvalidDataException($"Unsupported tile palette format/version '{Format}' v{Version}.");
        if (string.IsNullOrWhiteSpace(Name)) Name = "Tile Palette";
        Columns = Math.Max(1, Columns);
        Rows = Math.Max(1, Rows);
        foreach (var tile in Tiles) tile.Validate();
        var duplicate = Tiles.GroupBy(tile => tile.Id).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException($"Tile ID {duplicate.Key} is duplicated.");
        Tiles = Tiles.OrderBy(tile => tile.Id).ToList();
    }

    public void SliceAtlas(int columns, int rows, int tileCount = -1)
    {
        Columns = Math.Max(1, columns);
        Rows = Math.Max(1, rows);
        var count = tileCount < 0 ? checked(Columns * Rows) : Math.Clamp(tileCount, 0, checked(Columns * Rows));
        var previous = Tiles.ToDictionary(tile => tile.Id);
        var width = 1f / Columns;
        var height = 1f / Rows;
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
                    UvX = (float)uv.x,
                    UvY = (float)uv.y,
                    UvWidth = (float)uv.width,
                    UvHeight = (float)uv.height,
                    Red = previous?.Red ?? byte.MaxValue,
                    Green = previous?.Green ?? byte.MaxValue,
                    Blue = previous?.Blue ?? byte.MaxValue,
                    Alpha = previous?.Alpha ?? byte.MaxValue,
                    HasCollider = previous?.HasCollider ?? false
                };
            }).ToList();
    }

    public static TilePalette Load(string path)
    {
        var palette = YamlUtility.Load<TilePalette>(TilePalettePath.Resolve(path));
        palette.Validate();
        return palette;
    }

    public void Save(string path)
    {
        Validate();
        YamlUtility.Save(this, TilePalettePath.Resolve(path));
    }
}
