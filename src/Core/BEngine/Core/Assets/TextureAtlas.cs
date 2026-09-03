using BEngine.Documents;

namespace BEngine;

[EditorIcon("Icons/Assets/AssetAtlas.png")]
[CreateAssetMenu(fileName = "New Texture Atlas", menuName = "2D/Texture Atlas", order = 250)]
public sealed class TextureAtlas : BAsset
{
    public string Format { get; set; } = "BEngine.TextureAtlas";
    public string Texture { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int MaxSize { get; set; } = 2048;
    public int Padding { get; set; } = 2;
    public int Extrude { get; set; } = 1;
    [HideInInspector]
    public Sprite[] Sources { get; set; } = [];
    public List<TextureAtlasSprite> Sprites { get; set; } = [];

    public TextureAtlasSprite? Find(string nameOrSource)
    {
        if (string.IsNullOrWhiteSpace(nameOrSource)) return null;
        var value = nameOrSource.Replace('\\', '/').Trim();
        return Sprites.FirstOrDefault(sprite => sprite.Name.Equals(value, StringComparison.Ordinal)) ??
               Sprites.FirstOrDefault(sprite => sprite.Source.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<Sprite> LoadReferencedSprites() => Sources;

    public bool TryGetUv(string nameOrSource, out Rect uv)
    {
        var sprite = Find(nameOrSource);
        if (sprite is null || Width <= 0 || Height <= 0) { uv = new Rect(0, 0, 1, 1); return false; }
        uv = sprite.NormalizedUv(Width, Height); return true;
    }

    public void Validate()
    {
        if (!Format.Equals("BEngine.TextureAtlas", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported texture atlas format '{Format}'.");
        MaxSize = Math.Clamp(MaxSize, 32, 16384); Padding = Math.Clamp(Padding, 0, 64);
        Extrude = Math.Clamp(Extrude, 0, Padding); Sources ??= []; Sprites ??= [];
        var duplicate = Sources.Where(source => source is not null)
            .GroupBy(SourceIdentity, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Key.Length > 0 && group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"Texture atlas Sprite source '{duplicate.Key}' is duplicated.");
        if (Sprites.Count == 0) { Width = Math.Max(0, Width); Height = Math.Max(0, Height); return; }
        if (string.IsNullOrWhiteSpace(Texture) || Width <= 0 || Height <= 0)
            throw new InvalidDataException("A built texture atlas requires a texture path and positive dimensions.");
        foreach (var sprite in Sprites) sprite.Validate(Width, Height);
    }

    public static TextureAtlas Load(string path) => Document<TextureAtlas>
        .Read(TextureAtlasPath.Resolve(path), static sourcePath =>
        {
            var atlas = YamlUtility.Load<TextureAtlas>(sourcePath);
            atlas.Validate();
            return atlas;
        })
        .ToAsset();

    internal static TextureAtlas Deserialize(string yaml) => Document<TextureAtlas>
        .Parse(yaml, static contents =>
        {
            var atlas = YamlUtility.Deserialize<TextureAtlas>(contents);
            atlas.Validate();
            return atlas;
        })
        .ToAsset();

    public void Save(string path) => Document<TextureAtlas>.FromAsset(this)
        .Write(TextureAtlasPath.Resolve(path), static (atlas, destination) =>
        {
            atlas.Validate();
            YamlUtility.Save(atlas, destination);
        });

    internal static string SourceIdentity(Sprite sprite) => !string.IsNullOrWhiteSpace(sprite.OwnerGuid)
        ? $"{sprite.OwnerGuid}:{sprite.LocalIdentifier}" : sprite.assetPath.Replace('\\', '/').Trim();
}
