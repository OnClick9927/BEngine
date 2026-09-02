namespace BEngine;

internal sealed class TextureAtlasYamlData
{
    public string Format { get; set; } = "BEngine.TextureAtlas";
    public string Texture { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int MaxSize { get; set; } = 2048;
    public int Padding { get; set; } = 2;
    public int Extrude { get; set; } = 1;
    public List<TextureAtlasSourceYamlData> Sources { get; set; } = [];
    public List<TextureAtlasSprite> Sprites { get; set; } = [];
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public HideFlags HideFlags { get; set; }

    // Read-only compatibility with the previous versioned/reference-list formats.
    public int? Version { get; set; }
    public List<string>? SpriteReferences { get; set; }

    internal static TextureAtlasYamlData FromAsset(TextureAtlas atlas)
    {
        var sources = atlas.Sources.Select(TextureAtlasSourceYamlData.FromSprite).ToList();
        return new TextureAtlasYamlData
        {
            Format = atlas.Format,
            Texture = atlas.Texture,
            Width = atlas.Width,
            Height = atlas.Height,
            MaxSize = atlas.MaxSize,
            Padding = atlas.Padding,
            Extrude = atlas.Extrude,
            Sources = sources,
            Sprites = atlas.Sprites,
            Id = atlas.Id,
            Name = atlas.name,
            HideFlags = atlas.hideFlags
        };
    }

    internal TextureAtlas ToAsset()
    {
        Sources ??= [];
        Sprites ??= [];
        var sources = Sources.Select(source => source.ToSprite()).ToList();
        if (sources.Count == 0 && SpriteReferences is { Count: > 0 })
            sources.AddRange(SpriteReferences.Select(TextureAtlasSourceYamlData.FromLegacyReference)
                .Select(source => source.ToSprite()));
        var atlas = new TextureAtlas
        {
            Format = Format,
            Texture = Texture,
            Width = Width,
            Height = Height,
            MaxSize = MaxSize,
            Padding = Padding,
            Extrude = Extrude,
            Sources = sources.ToArray(),
            Sprites = Sprites,
            name = Name,
            hideFlags = HideFlags
        };
        if (Id != Guid.Empty) atlas.Id = Id;
        return atlas;
    }
}
