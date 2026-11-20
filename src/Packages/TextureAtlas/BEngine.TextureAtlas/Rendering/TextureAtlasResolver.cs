using System.Runtime.CompilerServices;

namespace BEngine;

internal static class TextureAtlasResolver
{
    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, PackedSprite> ReverseIndex = new(StringComparer.OrdinalIgnoreCase);
    private static bool _indexBuilt;
    private static readonly ConditionalWeakTable<Sprite, ResolvedSprite> Resolved = new();
    private sealed class ResolvedSprite
    {
        internal string Source = "", Entry = "", Owner = "", Path = "", Texture = "";
        internal long Local;
        internal TextureAtlas? Atlas;
        internal TextureAtlasSprite? Region;
        internal SpriteRenderData2D Data;
        internal int AtlasWidth, AtlasHeight, X, Y, Width, Height;

        internal bool MatchesPackedData(TextureAtlas atlas, TextureAtlasSprite region) =>
            Data.Texture == atlas.Texture && AtlasWidth == atlas.Width && AtlasHeight == atlas.Height &&
            X == region.X && Y == region.Y && Width == region.Width && Height == region.Height &&
            Data.Pivot.x == region.PivotX && Data.Pivot.y == region.PivotY;

        internal void CapturePackedData(TextureAtlas atlas, TextureAtlasSprite region)
        {
            AtlasWidth = atlas.Width; AtlasHeight = atlas.Height;
            X = region.X; Y = region.Y; Width = region.Width; Height = region.Height;
        }
    }

    internal static bool TryResolve(Sprite sprite, out SpriteRenderData2D data)
    {
        // Player assets change through resource invalidation, not filesystem polling.
        // Weak keys keep unloaded scenes collectible. Editor lookups retain live
        // atlas-file checks so an atlas repack is visible without restarting play.
        if (!Application.isEditor && Resolved.TryGetValue(sprite, out var cached) &&
            cached.Source == sprite.packedSource && cached.Entry == sprite.packedEntry &&
            cached.Owner == sprite.OwnerGuid && cached.Local == sprite.LocalIdentifier &&
            cached.Path == sprite.assetPath && cached.Texture == sprite.Texture &&
            cached.Atlas is { isDestroyed: false } && cached.Region is { } region &&
            // A hit must still observe mutable atlas/region data. Compare the inputs
            // instead of rebuilding UVs (four fixed-point divisions) just to test
            // whether the already resolved value is unchanged.
            cached.MatchesPackedData(cached.Atlas, region))
        { data = cached.Data; return true; }
        if (!TryGetPackedAtlas(sprite, out var reference, out var atlas, out var packed))
        { data = default; return false; }
        data = ResolvePacked(reference, atlas, packed);
        if (!Application.isEditor)
        {
            var entry = Resolved.GetValue(sprite, static _ => new());
            entry.Source = sprite.packedSource; entry.Entry = sprite.packedEntry;
            entry.Owner = sprite.OwnerGuid; entry.Local = sprite.LocalIdentifier;
            entry.Path = sprite.assetPath; entry.Texture = sprite.Texture;
            entry.Atlas = atlas; entry.Region = packed; entry.Data = data;
            entry.CapturePackedData(atlas, packed);
        }
        return true;
    }

    internal static int IndexBuildCount { get; private set; }

    internal static SpriteRenderData2D Resolve(Sprite? sprite)
    {
        if (sprite is null || sprite.isDestroyed) return SpriteRenderData2D.Solid;
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
        if (!string.IsNullOrWhiteSpace(sprite.packedSource))
        {
            try
            {
                var path = sprite.packedSource;
                if (TryLoadAtlas(path, out atlas) && atlas.Find(sprite.packedEntry) is { } packedEntry)
                {
                    atlasReference = sprite.packedSource;
                    region = packedEntry;
                    return true;
                }
            }
            catch (Exception exception) when (IsAssetReadException(exception)) { }
        }

        EnsureReverseIndex();
        var identity = TextureAtlas.SourceIdentity(sprite);
        if (identity.Length > 0 && ReverseIndex.TryGetValue(identity, out var packed) && !packed.Atlas.isDestroyed)
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
                return Matches6162();
                [global::BEngine.SkipLoopGuard]
                global::BEngine.Sprite? Matches6162()
                {
                    var matchSource6162 = atlas.Sources;
                    ArgumentNullException.ThrowIfNull(matchSource6162, "source");
                    global::System.ReadOnlySpan<global::BEngine.Sprite> matchValues6162 = matchSource6162;
                    foreach (var source in matchValues6162)
                    {
                        if (source.name.Equals(reference, StringComparison.Ordinal) || TextureAtlas.SourceIdentity(source).Equals(reference, StringComparison.OrdinalIgnoreCase))
                            return source;
                    }
                
                    return default!;
                }
            }
            catch (Exception exception) when (IsAssetReadException(exception))
            {
                return null;
            }
        }

        try
        {
            return RuntimeObjectCodecs.ReadReference(typeof(Sprite), reference) as Sprite;
        }
        catch (Exception exception) when (IsAssetReadException(exception))
        {
            return null;
        }
    }

    internal static void Clear()
    {
        Resolved.Clear();
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
            var atlasReference = RuntimeAssetReferences.ToReferencePath(path);
            var regions = BuildIndex7981(atlas.Sprites.Where(item => !string.IsNullOrWhiteSpace(item.Source)).GroupBy(item => item.Source, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            [global::BEngine.SkipLoopGuard]
            global::System.Collections.Generic.Dictionary<string, global::BEngine.TextureAtlasSprite> BuildIndex7981(global::System.Collections.Generic.IEnumerable<global::System.Linq.IGrouping<string, global::BEngine.TextureAtlasSprite>> indexSource7981, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer7981)
            {
                ArgumentNullException.ThrowIfNull(indexSource7981, "source");
                var knownCount = indexSource7981.TryGetNonEnumeratedCount(out var capacity);
                if (knownCount && capacity == 0)
                    return new global::System.Collections.Generic.Dictionary<string, global::BEngine.TextureAtlasSprite>(indexComparer7981);
                global::System.ReadOnlySpan<global::System.Linq.IGrouping<string, global::BEngine.TextureAtlasSprite>> values;
                if (knownCount && indexSource7981 is global::System.Linq.IGrouping<string, global::BEngine.TextureAtlasSprite>[] array)
                    values = array;
                else if (knownCount && indexSource7981 is global::System.Collections.Generic.List<global::System.Linq.IGrouping<string, global::BEngine.TextureAtlasSprite>> list)
                    values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
                else
                {
                    var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.TextureAtlasSprite>(capacity, indexComparer7981);
                    foreach (var item in indexSource7981)
                        result.Add(IndexKey7981(item), IndexValue7981(item));
                    return result;
                }
            
                {
                    var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.TextureAtlasSprite>(values.Length, indexComparer7981);
                    foreach (var item in values)
                        result.Add(IndexKey7981(item), IndexValue7981(item));
                    return result;
                }
            
                string IndexKey7981(global::System.Linq.IGrouping<string, global::BEngine.TextureAtlasSprite> group) => group.Key;
                global::BEngine.TextureAtlasSprite IndexValue7981(global::System.Linq.IGrouping<string, global::BEngine.TextureAtlasSprite> group) => group.First();
            }
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
        var fileSystem = RuntimeServices.GetRequiredService<global::BEngine.Platform.FileUtility>();
        var pathsService = RuntimeServices.GetRequiredService<global::BEngine.Platform.PathUtility>();
        var dataPath = Application.dataPath;
        if (string.IsNullOrWhiteSpace(dataPath) || !fileSystem.DirectoryExists(dataPath)) return [];
        var projectRoot = pathsService.GetDirectoryName(
            pathsService.TrimEndingDirectorySeparator(pathsService.GetFullPath(dataPath)));
        var roots = projectRoot is null
            ? [dataPath]
            : new[] { dataPath, pathsService.Combine(projectRoot, "Packages") };
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(fileSystem.DirectoryExists))
        {
            try
            {
                foreach (var path in fileSystem.EnumerateFiles(
                             root, "*.atlas.yaml", SearchOption.AllDirectories))
                    paths.Add(pathsService.GetFullPath(path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool TryLoadAtlas(string path, out TextureAtlas atlas)
    {
        var fileSystem = RuntimeServices.GetRequiredService<global::BEngine.Platform.FileUtility>();
        atlas = null!;
        if (!Application.isEditor && Cache.TryGetValue(path, out var loaded) && !loaded.Atlas.isDestroyed)
        { atlas = loaded.Atlas; return true; }
        if (!fileSystem.FileExists(path))
        {
            if (Cache.TryGetValue(path, out var resourceEntry) && resourceEntry.Length == -1 && !resourceEntry.Atlas.isDestroyed)
            { atlas = resourceEntry.Atlas; return true; }
            atlas = Resources.Load<TextureAtlas>(path)!;
            if (atlas is null || atlas.isDestroyed) return false;
            Cache[path] = new CacheEntry(DateTime.MinValue, -1, atlas);
            return true;
        }
        var writeTime = fileSystem.GetLastWriteTimeUtc(path);
        var length = fileSystem.GetFileLength(path);
        if (!Cache.TryGetValue(path, out var cached) || cached.Atlas.isDestroyed ||
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
        ResolvePackedCached(NormalizeIdentity(atlasReference), atlas, region);

    private static SpriteRenderData2D ResolvePackedCached(string identity, TextureAtlas atlas, TextureAtlasSprite region) =>
        new(atlas.Texture, identity, region.NormalizedUv(atlas.Width, atlas.Height),
            region.pivot, true) { PixelSize = new Vector2(region.Width, region.Height) };

    private static string NormalizeIdentity(string reference)
    {
        try { return RuntimeAssetReferences.ResolvePath(reference).Replace('\\', '/'); }
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
