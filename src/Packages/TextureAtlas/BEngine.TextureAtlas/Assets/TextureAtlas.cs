using BEngine.Serialization;
using System.Runtime.CompilerServices;

namespace BEngine;

[EditorIcon("Icons/Assets/AssetAtlas.png")]
[CreateAssetMenu(fileName = "New Texture Atlas", menuName = "2D/Texture Atlas", order = 250)]
public sealed class TextureAtlas : BAsset
{
    internal void RestoreIdentity(Guid identity) => RestorePersistentIdentity(identity);
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
        [global::BEngine.SkipLoopGuard]
        global::BEngine.TextureAtlasSprite? Query955(global::System.Collections.Generic.IEnumerable<global::BEngine.TextureAtlasSprite> querySource955)
        {
            ArgumentNullException.ThrowIfNull(querySource955, "source");
            bool Matches955(global::BEngine.TextureAtlasSprite sprite) => sprite.Name.Equals(value, StringComparison.Ordinal);
            global::System.ReadOnlySpan<global::BEngine.TextureAtlasSprite> queryValues955;
            if (querySource955.GetType() == typeof(global::BEngine.TextureAtlasSprite[]))
                queryValues955 = (global::BEngine.TextureAtlasSprite[])querySource955;
            else if (querySource955.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.TextureAtlasSprite>))
                queryValues955 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.TextureAtlasSprite>)querySource955);
            else
            {
                foreach (var sprite in querySource955)
                    if (Matches955(sprite))
                        return sprite;
                return default!;
            }
        
            foreach (var sprite in queryValues955)
                if (Matches955(sprite))
                    return sprite;
            return default!;
        }
        
        [global::BEngine.SkipLoopGuard]
        global::BEngine.TextureAtlasSprite? Query1060(global::System.Collections.Generic.IEnumerable<global::BEngine.TextureAtlasSprite> querySource1060)
        {
            ArgumentNullException.ThrowIfNull(querySource1060, "source");
            bool Matches1060(global::BEngine.TextureAtlasSprite sprite) => sprite.Source.Equals(value, StringComparison.OrdinalIgnoreCase);
            global::System.ReadOnlySpan<global::BEngine.TextureAtlasSprite> queryValues1060;
            if (querySource1060.GetType() == typeof(global::BEngine.TextureAtlasSprite[]))
                queryValues1060 = (global::BEngine.TextureAtlasSprite[])querySource1060;
            else if (querySource1060.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.TextureAtlasSprite>))
                queryValues1060 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.TextureAtlasSprite>)querySource1060);
            else
            {
                foreach (var sprite in querySource1060)
                    if (Matches1060(sprite))
                        return sprite;
                return default!;
            }
        
            foreach (var sprite in queryValues1060)
                if (Matches1060(sprite))
                    return sprite;
            return default!;
        }
        
        return Query955(Sprites) ?? Query1060(Sprites);
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
        var duplicate = Query2657(Sources.Where(source => source is not null).GroupBy(SourceIdentity, StringComparer.OrdinalIgnoreCase));
        [global::BEngine.SkipLoopGuard]
        global::System.Linq.IGrouping<string, global::BEngine.Sprite>? Query2657(global::System.Collections.Generic.IEnumerable<global::System.Linq.IGrouping<string, global::BEngine.Sprite>> querySource2657)
        {
            ArgumentNullException.ThrowIfNull(querySource2657, "source");
            bool Matches2657(global::System.Linq.IGrouping<string, global::BEngine.Sprite> group) => group.Key.Length > 0 && group.Count() > 1;
            global::System.ReadOnlySpan<global::System.Linq.IGrouping<string, global::BEngine.Sprite>> queryValues2657;
            if (querySource2657.GetType() == typeof(global::System.Linq.IGrouping<string, global::BEngine.Sprite>[]))
                queryValues2657 = (global::System.Linq.IGrouping<string, global::BEngine.Sprite>[])querySource2657;
            else if (querySource2657.GetType() == typeof(global::System.Collections.Generic.List<global::System.Linq.IGrouping<string, global::BEngine.Sprite>>))
                queryValues2657 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::System.Linq.IGrouping<string, global::BEngine.Sprite>>)querySource2657);
            else
            {
                foreach (var group in querySource2657)
                    if (Matches2657(group))
                        return group;
                return default!;
            }
        
            foreach (var group in queryValues2657)
                if (Matches2657(group))
                    return group;
            return default!;
        }
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
