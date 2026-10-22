using BEngine.Serialization;
using System.Runtime.CompilerServices;

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

    /// <summary>Creates a sprite bound to this atlas, including atlases loaded from a resource provider.</summary>
    public Sprite? GetSprite(string name)
    {
        var region = Find(name);
        if (region is null) return null;
        var sprite = Sprite.Create(Texture, region.pivot, assetPath + "#" + region.Name,
            source: assetPath, entry: region.Name);
        var identity = region.Source.Split(':');
        if (identity.Length == 2 && Guid.TryParse(identity[0], out var owner) && long.TryParse(identity[1], out var local))
            sprite.BindSourceIdentity(owner.ToString("N"), local, assetPath + "#" + region.Name);
        sprite.name = region.Name;
        return sprite;
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

    private static readonly ConditionalWeakTable<Sprite, CachedSourceIdentity> SourceIdentities = new();
    private sealed class CachedSourceIdentity
    {
        internal string? Owner, Path;
        internal long Local;
        internal string Value = string.Empty;
    }

    internal static string SourceIdentity(Sprite sprite)
    {
        var cached = SourceIdentities.GetValue(sprite, static _ => new());
        var owner = sprite.OwnerGuid;
        var path = sprite.assetPath;
        if (cached.Owner == owner && cached.Local == sprite.LocalIdentifier && cached.Path == path)
            return cached.Value;
        // Sprites and their source references remain mutable in the editor and
        // at runtime. Cache the formatted key only while all identity inputs
        // agree; weak keys let an unloaded asset release its cached key too.
        cached.Owner = owner; cached.Local = sprite.LocalIdentifier; cached.Path = path;
        return cached.Value = !string.IsNullOrWhiteSpace(owner)
            ? $"{owner}:{sprite.LocalIdentifier}" : path.Replace('\\', '/').Trim();
    }
}
