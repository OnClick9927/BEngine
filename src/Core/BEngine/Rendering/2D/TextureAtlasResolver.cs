namespace BEngine;

internal static class TextureAtlasResolver
{
    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, PackedSprite> ReverseIndex =
        new(StringComparer.OrdinalIgnoreCase);
    private static bool _indexDirty = true;
    internal static int IndexBuildCount { get; private set; }

    internal static SpriteRenderData2D Resolve(Sprite? sprite)
    {
        if (sprite is null) return SpriteRenderData2D.Solid;
        if (!string.IsNullOrWhiteSpace(sprite.packedAtlas))
            return Resolve(sprite.packedAtlas, sprite.packedRegion);
        if (!string.IsNullOrWhiteSpace(sprite.assetPath) &&
            TryFindPackedSprite(sprite.assetPath, out var packed))
            return ResolvePacked(packed.AtlasReference, packed.Atlas, packed.Region);
        if (string.IsNullOrWhiteSpace(sprite.Texture))
            return Missing(string.Empty, sprite.assetPath.Length > 0 ? sprite.assetPath : sprite.name);
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
                if (TryLoadAtlas(path, out atlas) && atlas.Find(sprite.packedRegion) is { } found)
                {
                    atlasReference = sprite.packedAtlas;
                    region = found;
                    return true;
                }
            }
            catch (Exception exception) when (IsAssetReadException(exception)) { }
        }
        else if (!string.IsNullOrWhiteSpace(sprite.assetPath) &&
                 TryFindPackedSprite(sprite.assetPath, out var packed))
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
        if (string.IsNullOrWhiteSpace(atlasAsset))
            return string.IsNullOrWhiteSpace(sprite)
                ? SpriteRenderData2D.Solid
                : new SpriteRenderData2D(sprite.Trim(), sprite.Trim(), new Rect(0, 0, 1, 1),
                    new Vector2(Fix64.Half, Fix64.Half), true);
        try
        {
            var path = TextureAtlasPath.Resolve(atlasAsset);
            if (!TryLoadAtlas(path, out var atlas)) return Missing(atlasAsset, sprite);
            var region = atlas.Find(sprite);
            if (region is null || string.IsNullOrWhiteSpace(atlas.Texture)) return Missing(atlasAsset, sprite);
            return ResolvePacked(atlasAsset, atlas, region);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or ArgumentException or FormatException or
                                          OverflowException or YamlDotNet.Core.YamlException)
        {
            return Missing(atlasAsset, sprite);
        }
    }

    internal static Sprite? LoadSpriteReference(string reference, string legacyAtlas = "")
    {
        reference = reference?.Replace('\\', '/').Trim() ?? string.Empty;
        legacyAtlas = legacyAtlas?.Replace('\\', '/').Trim() ?? string.Empty;
        if (reference.Length == 0) return null;
        if (legacyAtlas.Length > 0)
            return LoadPackedSpriteReference(legacyAtlas, reference);

        var separator = reference.LastIndexOf('#');
        if (separator > 0 && separator < reference.Length - 1 &&
            reference[..separator].EndsWith(".atlas.yaml", StringComparison.OrdinalIgnoreCase))
            return LoadPackedSpriteReference(reference[..separator], reference[(separator + 1)..]);

        if (reference.EndsWith(".sprite.yaml", StringComparison.OrdinalIgnoreCase))
        {
            try { return Sprite.Load(reference); }
            catch (Exception exception) when (IsAssetReadException(exception))
            {
                return Sprite.FromTexture(string.Empty, HalfPivot, reference);
            }
        }
        try { return BAsset.Load<Sprite>(reference); }
        catch (Exception exception) when (IsAssetReadException(exception)) { return null; }
    }

    internal static void Clear()
    {
        Cache.Clear();
        ReverseIndex.Clear();
        _indexDirty = true;
    }

    private static Sprite LoadPackedSpriteReference(string atlasReference, string regionName)
    {
        try
        {
            var path = TextureAtlasPath.Resolve(atlasReference);
            if (TryLoadAtlas(path, out var atlas) && atlas.Find(regionName) is { } region)
            {
                if (atlas.Version < 2)
                    return Sprite.FromTexture(region.Source, region.pivot,
                        $"{atlasReference}#{region.Name}", atlasReference, region.Name);
                if (region.Source.EndsWith(".sprite.yaml", StringComparison.OrdinalIgnoreCase))
                {
                    var sprite = Sprite.Load(region.Source);
                    sprite.packedAtlas = atlasReference;
                    sprite.packedRegion = region.Name;
                    return sprite;
                }
                var imported = Guid.TryParse(region.Source, out _)
                    ? BAsset.LoadByGuid<Sprite>(region.Source)
                    : BAsset.Load<Sprite>(region.Source);
                if (imported is not null)
                    return Sprite.FromTexture(imported.Texture, region.pivot,
                        $"{atlasReference}#{region.Name}", atlasReference, region.Name);
            }
        }
        catch (Exception exception) when (IsAssetReadException(exception)) { }
        return Sprite.FromTexture(string.Empty, HalfPivot,
            $"{atlasReference}#{regionName}", atlasReference, regionName);
    }

    private static bool TryFindPackedSprite(string spriteReference, out PackedSprite packed)
    {
        EnsureReverseIndex();
        try { return ReverseIndex.TryGetValue(AssetReferencePath.Key(spriteReference), out packed!); }
        catch (ArgumentException)
        {
            packed = null!;
            return false;
        }
    }

    private static void EnsureReverseIndex()
    {
        if (!_indexDirty) return;
        _indexDirty = false;
        IndexBuildCount++;
        ReverseIndex.Clear();
        var dataPath = Application.dataPath;
        if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath)) return;
        var paths = EnumerateAtlasPaths(dataPath);
        foreach (var path in paths)
        {
            if (!TryLoadAtlas(path, out var atlas)) continue;
            if (atlas.Version < 2 || atlas.SpriteReferences.Count == 0) continue;
            var atlasReference = AssetReferencePath.ToReference(path);
            var activeReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in atlas.SpriteReferences.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var imported = Guid.TryParse(reference, out _)
                        ? BAsset.LoadByGuid<Sprite>(reference)
                        : BAsset.Load<Sprite>(ReferenceForLoad(reference, path));
                    if (imported is not null && !string.IsNullOrWhiteSpace(imported.assetPath))
                        activeReferences.Add(AssetReferencePath.Key(imported.assetPath));
                }
                catch (Exception exception) when (IsAssetReadException(exception)) { }
            }
            foreach (var region in atlas.Sprites.OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(region.Source)) continue;
                try
                {
                    var key = SourceReferenceKey(region.Source, path);
                    if (key.Length == 0) continue;
                    if (!activeReferences.Contains(key)) continue;
                    ReverseIndex.TryAdd(key, new PackedSprite(atlasReference, atlas, region));
                }
                catch (ArgumentException) { }
            }
        }
    }

    private static string[] EnumerateAtlasPaths(string dataPath)
    {
        var assetsRoot = Path.GetFullPath(dataPath);
        var projectRoot = Directory.GetParent(assetsRoot)?.FullName;
        var roots = projectRoot is null
            ? [assetsRoot]
            : new[] { assetsRoot, Path.Combine(projectRoot, "Packages") };
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
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

    private static string ReferenceKey(string reference, string atlasPath)
    {
        if (Path.IsPathRooted(reference) || HasProjectPrefix(reference, "Assets") ||
            HasProjectPrefix(reference, "Packages"))
            return AssetReferencePath.Key(reference);
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(atlasPath)!,
            reference.Replace('/', Path.DirectorySeparatorChar))).Replace('\\', '/');
    }

    private static string SourceReferenceKey(string reference, string atlasPath)
    {
        if (!Guid.TryParse(reference, out _)) return ReferenceKey(reference, atlasPath);
        var sprite = BAsset.LoadByGuid<Sprite>(reference);
        return sprite is null || string.IsNullOrWhiteSpace(sprite.assetPath)
            ? string.Empty
            : AssetReferencePath.Key(sprite.assetPath);
    }

    private static string ReferenceForLoad(string reference, string atlasPath) =>
        Path.IsPathRooted(reference) || HasProjectPrefix(reference, "Assets") ||
        HasProjectPrefix(reference, "Packages")
            ? reference
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(atlasPath)!,
                reference.Replace('/', Path.DirectorySeparatorChar)));

    private static bool HasProjectPrefix(string reference, string prefix) =>
        reference.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
        reference.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase) ||
        reference.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase);

    private static SpriteRenderData2D ResolvePacked(
        string atlasReference, TextureAtlas atlas, TextureAtlasSprite region) =>
        new(atlas.Texture, NormalizeIdentity(atlasReference),
            region.NormalizedUv(atlas.Width, atlas.Height), region.pivot, true);

    private static string NormalizeIdentity(string reference)
    {
        try { return AssetReferencePath.Key(reference); }
        catch (ArgumentException) { return reference.Trim(); }
    }

    private static bool IsAssetReadException(Exception exception) => exception is IOException or
        UnauthorizedAccessException or InvalidDataException or ArgumentException or FormatException or
        OverflowException or YamlDotNet.Core.YamlException;

    private static Vector2 HalfPivot => new(Fix64.Half, Fix64.Half);

    private static SpriteRenderData2D Missing(string atlas, string sprite)
    {
        var identity = $"missing:{atlas.Trim()}#{sprite.Trim()}";
        return new SpriteRenderData2D(identity, identity, new Rect(0, 0, 1, 1),
            new Vector2(Fix64.Half, Fix64.Half), true);
    }

    private sealed record CacheEntry(DateTime WriteTimeUtc, long Length, TextureAtlas Atlas);
    private sealed record PackedSprite(
        string AtlasReference, TextureAtlas Atlas, TextureAtlasSprite Region);
}
