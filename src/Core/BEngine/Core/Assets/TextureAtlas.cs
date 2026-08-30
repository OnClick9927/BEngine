namespace BEngine;

[CreateAssetMenu(fileName = "New Texture Atlas", menuName = "2D/Texture Atlas", order = 250)]
public sealed class TextureAtlas : BAsset
{
    public string Format { get; set; } = "BEngine.TextureAtlas";
    public int Version { get; set; } = 2;
    public string Texture { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int MaxSize { get; set; } = 2048;
    public int Padding { get; set; } = 2;
    public int Extrude { get; set; } = 1;
    public List<string> SpriteReferences { get; set; } = [];
    // Kept so version 1 atlases continue to load and can be rebuilt without rewriting source assets.
    public List<TextureAtlasSource> Sources { get; set; } = [];
    public List<TextureAtlasSprite> Sprites { get; set; } = [];

    public TextureAtlasSprite? Find(string nameOrSource)
    {
        if (string.IsNullOrWhiteSpace(nameOrSource)) return null;
        var value = nameOrSource.Replace('\\', '/').Trim();
        return Sprites.FirstOrDefault(sprite => sprite.Name.Equals(value, StringComparison.Ordinal)) ??
               Sprites.FirstOrDefault(sprite => sprite.Source.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<Sprite> LoadReferencedSprites() => SpriteReferences
        .Select(reference => BAsset.Load<Sprite>(reference) ?? throw new InvalidDataException(
            $"Texture atlas Sprite reference '{reference}' is not a legacy Sprite asset or a texture imported as Sprite."))
        .ToArray();

    public bool TryGetUv(string nameOrSource, out Rect uv)
    {
        var sprite = Find(nameOrSource);
        if (sprite is null || Width <= 0 || Height <= 0)
        {
            uv = new Rect(0, 0, 1, 1);
            return false;
        }
        uv = sprite.NormalizedUv(Width, Height);
        return true;
    }

    public void Validate()
    {
        if (!Format.Equals("BEngine.TextureAtlas", StringComparison.Ordinal) || Version is < 1 or > 2)
            throw new InvalidDataException($"Unsupported texture atlas format/version '{Format}' v{Version}.");
        MaxSize = Math.Clamp(MaxSize, 32, 16384);
        Padding = Math.Clamp(Padding, 0, 64);
        Extrude = Math.Clamp(Extrude, 0, Padding);
        SpriteReferences ??= [];
        Sources ??= [];
        Sprites ??= [];
        for (var index = 0; index < SpriteReferences.Count; index++)
        {
            var reference = SpriteReferences[index]?.Replace('\\', '/').Trim() ?? string.Empty;
            if (reference.Length == 0)
                throw new InvalidDataException("Texture atlas Sprite references cannot be empty.");
            SpriteReferences[index] = reference;
        }
        RejectDuplicates(SpriteReferences, "Sprite reference", StringComparer.OrdinalIgnoreCase);
        foreach (var source in Sources) source.Validate();
        RejectDuplicates(Sources.Select(source => source.Name), "source name");
        RejectDuplicates(Sources.Select(source => source.Path), "source path", StringComparer.OrdinalIgnoreCase);
        if (Sprites.Count == 0)
        {
            Width = Math.Max(0, Width);
            Height = Math.Max(0, Height);
            return;
        }
        if (string.IsNullOrWhiteSpace(Texture) || Width <= 0 || Height <= 0)
            throw new InvalidDataException("A built texture atlas requires a texture path and positive dimensions.");
        foreach (var sprite in Sprites) sprite.Validate(Width, Height);
        RejectDuplicates(Sprites.Select(sprite => sprite.Name), "sprite name");
    }

    public static TextureAtlas Load(string path)
    {
        var atlas = YamlUtility.Load<TextureAtlas>(TextureAtlasPath.Resolve(path));
        atlas.Validate();
        return atlas;
    }

    public void Save(string path)
    {
        Validate();
        YamlUtility.Save(this, TextureAtlasPath.Resolve(path));
    }

    private static void RejectDuplicates(IEnumerable<string> values, string label,
        IEqualityComparer<string>? comparer = null)
    {
        var duplicate = values.GroupBy(value => value, comparer ?? StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"Texture atlas {label} '{duplicate.Key}' is duplicated.");
    }
}
