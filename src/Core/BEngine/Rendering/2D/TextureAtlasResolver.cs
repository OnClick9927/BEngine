namespace BEngine;

internal static class TextureAtlasResolver
{
    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, PackedSprite> ReverseIndex = new(StringComparer.OrdinalIgnoreCase);
    private static bool _indexBuilt;

    internal static int IndexBuildCount { get; private set; }

    internal static SpriteRenderData2D Resolve(Sprite? sprite)
    {
        if (sprite is null) return SpriteRenderData2D.Solid;
        if (TryGetPackedAtlas(sprite, out var atlasReference, out var atlas, out var region))
            return ResolvePacked(atlasReference, atlas, region);
        if (string.IsNullOrWhiteSpace(sprite.Texture)) return Missing(string.Empty, sprite.name);
        return new SpriteRenderData2D(sprite.Texture, NormalizeIdentity(sprite.Texture),
            new Rect(0, 0, 1, 1), sprite.pivot, true);
    }

    internal static bool TryGetPackedAtlas(
        Sprite sprite,
        out string atlasReference,
        out TextureAtlas atlas,
        out TextureAtlasSprite region)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        if (!string.IsNullOrWhiteSpace(sprite.packedAtlas))
        {
            try
            {
                var path = TextureAtlasPath.Resolve(sprite.packedAtlas);
                if (TryLoadAtlas(path, out atlas) && atlas.Find(sprite.packedRegion) is { } packedRegion)
                {
                    atlasReference = sprite.packedAtlas;
                    region = packedRegion;
                    return true;
                }
            }
            catch (Exception exception) when (IsAssetReadException(exception)) { }
        }

        EnsureReverseIndex();
        var identity = TextureAtlas.SourceIdentity(sprite);
        if (identity.Length > 0 && ReverseIndex.TryGetValue(identity, out var packed))
        {
            atlasReference = packed.AtlasReference;
            atlas = packed.Atlas;
            region = packed.Region;
            return true;
        }

        atlasReference = string.Empty;
        atlas = null!;
        region = null!;
        return false;
    }

    internal static SpriteRenderData2D Resolve(string atlasAsset, string sprite)
    {
        if (string.IsNullOrWhiteSpace(atlasAsset)) return Missing(atlasAsset, sprite);
        try
        {
            var path = TextureAtlasPath.Resolve(atlasAsset);
            if (!TryLoadAtlas(path, out var atlas) || atlas.Find(sprite) is not { } region)
                return Missing(atlasAsset, sprite);
            return ResolvePacked(atlasAsset, atlas, region);
        }
        catch (Exception exception) when (IsAssetReadException(exception))
        {
            return Missing(atlasAsset, sprite);
        }
    }

    internal static Sprite? LoadSpriteReference(string reference, string legacyAtlas = "")
    {
        if (!string.IsNullOrWhiteSpace(legacyAtlas))
        {
            try
            {
                var atlas = TextureAtlas.Load(legacyAtlas);
                return atlas.Sources.FirstOrDefault(source =>
                    source.name.Equals(reference, StringComparison.Ordinal) ||
                    TextureAtlas.SourceIdentity(source).Equals(reference, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception exception) when (IsAssetReadException(exception))
            {
                return null;
            }
        }

        try
        {
            return BAssetReferenceLoader.LoadObjectReference(reference, typeof(Sprite)) as Sprite;
        }
        catch (Exception exception) when (IsAssetReadException(exception))
        {
            return null;
        }
    }

    internal static void Clear()
    {
        Cache.Clear();
        ReverseIndex.Clear();
        _indexBuilt = false;
        RebuildIndex();
    }

    private static void EnsureReverseIndex()
    {
        if (!_indexBuilt) RebuildIndex();
    }

    private static void RebuildIndex()
    {
        _indexBuilt = true;
        IndexBuildCount++;
        ReverseIndex.Clear();
        foreach (var path in EnumerateAtlasPaths())
        {
            if (!TryLoadAtlas(path, out var atlas)) continue;
            var atlasReference = AssetReferencePath.ToReference(path);
            var regions = atlas.Sprites
                .Where(item => !string.IsNullOrWhiteSpace(item.Source))
                .GroupBy(item => item.Source, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var source in atlas.Sources)
            {
                var identity = TextureAtlas.SourceIdentity(source);
                if (identity.Length == 0 || !regions.TryGetValue(identity, out var region)) continue;
                ReverseIndex.TryAdd(identity, new PackedSprite(atlasReference, atlas, region));
            }
        }
    }

    private static string[] EnumerateAtlasPaths()
    {
        var dataPath = Application.dataPath;
        if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath)) return [];
        var projectRoot = Directory.GetParent(dataPath)?.FullName;
        var roots = projectRoot is null
            ? [dataPath]
            : new[] { dataPath, Path.Combine(projectRoot, "Packages") };
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*.atlas.yaml", SearchOption.AllDirectories))
                    paths.Add(Path.GetFullPath(path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool TryLoadAtlas(string path, out TextureAtlas atlas)
    {
        atlas = null!;
        if (!File.Exists(path)) return false;
        var writeTime = File.GetLastWriteTimeUtc(path);
        var length = new FileInfo(path).Length;
        if (!Cache.TryGetValue(path, out var cached) ||
            cached.WriteTimeUtc != writeTime || cached.Length != length)
        {
            cached = new CacheEntry(writeTime, length, TextureAtlas.Load(path));
            Cache[path] = cached;
        }
        atlas = cached.Atlas;
        return true;
    }

    private static SpriteRenderData2D ResolvePacked(
        string atlasReference,
        TextureAtlas atlas,
        TextureAtlasSprite region) =>
        new(atlas.Texture, NormalizeIdentity(atlasReference), region.NormalizedUv(atlas.Width, atlas.Height),
            region.pivot, true);

    private static string NormalizeIdentity(string reference)
    {
        try { return AssetReferencePath.Key(reference); }
        catch (ArgumentException) { return reference.Trim(); }
    }

    private static bool IsAssetReadException(Exception exception) => exception is IOException or
        UnauthorizedAccessException or InvalidDataException or ArgumentException or FormatException or
        OverflowException or YamlDotNet.Core.YamlException;

    private static SpriteRenderData2D Missing(string atlas, string sprite)
    {
        var identity = $"missing:{atlas.Trim()}#{sprite.Trim()}";
        return new SpriteRenderData2D(identity, identity, new Rect(0, 0, 1, 1),
            new Vector2(Fix64.Half, Fix64.Half), true);
    }

    private sealed record CacheEntry(DateTime WriteTimeUtc, long Length, TextureAtlas Atlas);
    private sealed record PackedSprite(string AtlasReference, TextureAtlas Atlas, TextureAtlasSprite Region);
}
