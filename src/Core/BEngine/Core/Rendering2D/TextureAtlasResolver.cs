namespace BEngine;

internal static class TextureAtlasResolver
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

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
            if (!File.Exists(path)) return Missing(atlasAsset, sprite);
            var writeTime = File.GetLastWriteTimeUtc(path);
            TextureAtlas atlas;
            lock (Sync)
            {
                if (!Cache.TryGetValue(path, out var cached) || cached.WriteTimeUtc != writeTime)
                {
                    cached = new CacheEntry(writeTime, TextureAtlas.Load(path));
                    Cache[path] = cached;
                }
                atlas = cached.Atlas;
            }
            var region = atlas.Find(sprite);
            if (region is null || string.IsNullOrWhiteSpace(atlas.Texture)) return Missing(atlasAsset, sprite);
            return new SpriteRenderData2D(atlas.Texture, atlasAsset.Trim(),
                region.NormalizedUv(atlas.Width, atlas.Height), region.pivot, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or ArgumentException or FormatException or
                                          OverflowException or YamlDotNet.Core.YamlException)
        {
            return Missing(atlasAsset, sprite);
        }
    }

    internal static void Clear()
    {
        lock (Sync) Cache.Clear();
    }

    private static SpriteRenderData2D Missing(string atlas, string sprite)
    {
        var identity = $"missing:{atlas.Trim()}#{sprite.Trim()}";
        return new SpriteRenderData2D(identity, identity, new Rect(0, 0, 1, 1),
            new Vector2(Fix64.Half, Fix64.Half), true);
    }

    private sealed record CacheEntry(DateTime WriteTimeUtc, TextureAtlas Atlas);
}
