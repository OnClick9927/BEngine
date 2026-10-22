using static BEngine.Editor.AssetPreview;
namespace BEngine.Editor;
public sealed class TextureAtlasPreviewProvider : IAssetPreviewProvider
{
    private static readonly Lock CacheGate = new();
    private static readonly Dictionary<string, CachedAtlas> AtlasCache = new(StringComparer.OrdinalIgnoreCase);
    static TextureAtlasPreviewProvider() => AssetPreview.CacheInvalidated += _ => { lock (CacheGate) AtlasCache.Clear(); };
    public bool CanPreview(BObject asset) => asset is TextureAtlas || asset is DefaultAsset d && IsAtlas(d) || asset is Sprite sprite && TextureAtlasResolver.TryGetPackedAtlas(sprite, out _, out _, out _);
    public AssetPreviewImage? GetPreview(BObject asset) => asset switch
    {
        TextureAtlas atlas => GetAtlasImage(atlas, AssetDatabase.GetAssetPath(atlas)),
        Sprite sprite => GetSpriteImage(sprite),
        DefaultAsset d when GetAtlas(d) is { Atlas: { } atlas } => GetAtlasImage(atlas, SourcePath(d)),
        _ => null
    };
    public string GetIcon(BObject asset) => "Icons/Assets/AssetAtlas.png";
    public string GetInfo(BObject asset) => asset switch
    {
        TextureAtlas atlas => $"{atlas.Width} x {atlas.Height} | {atlas.Sprites.Count} sprites",
        DefaultAsset d => AtlasInfo(d),
        Sprite sprite when TextureAtlasResolver.TryGetPackedAtlas(sprite, out var reference, out _, out _) => $"Sprite | Packed in {reference} | Pivot {sprite.PivotX:0.###}, {sprite.PivotY:0.###}",
        _ => string.Empty
    };
    public void Draw(BObject asset, Rect area)
    {
        if (asset is TextureAtlas atlas) DrawAtlas(atlas, AssetDatabase.GetAssetPath(atlas), area);
        else if (asset is Sprite sprite) DrawSprite(sprite, area);
        else if (asset is DefaultAsset d && GetAtlas(d) is { Atlas: { } loaded }) DrawAtlas(loaded, SourcePath(d), area);
        else DrawUnavailable(area, GetIcon(asset), "Atlas preview unavailable");
    }
    private static void DrawAtlas(TextureAtlas atlas, string contextPath, Rect area)
    {
        var image = GetAtlasImage(atlas, contextPath);
        if (image is null)
        {
            DrawUnavailable(area, EditorBuiltinIcons.Assets.Image, "Atlas texture unavailable");
            return;
        }

        var fitted = DrawImage(area, image.Value);
        if (atlas.Width <= 0 || atlas.Height <= 0) return;
        var selection = EditorStyles.selectionRect.normal.backgroundColor;
        var outline = new Color(selection.r, selection.g, selection.b, Fix64.FromDecimal(0.72m));
        foreach (var sprite in atlas.Sprites.Take(128))
        {
            var spriteRect = new Rect(
                fitted.x + fitted.width * sprite.X / atlas.Width,
                fitted.y + fitted.height * sprite.Y / atlas.Height,
                fitted.width * sprite.Width / atlas.Width,
                fitted.height * sprite.Height / atlas.Height);
            DrawBorder(spriteRect, outline);
        }
    }


    private static void DrawSprite(Sprite sprite, Rect area)
    {
        var image = GetSpriteImage(sprite);
        if (image is null)
        {
            DrawUnavailable(area, EditorBuiltinIcons.Assets.Image, "Sprite texture unavailable");
            return;
        }
        var fitted = DrawImage(area, image.Value);
        if (!TextureAtlasResolver.TryGetPackedAtlas(sprite, out _, out var atlas, out var region) ||
            atlas.Width <= 0 || atlas.Height <= 0) return;
        var regionRect = new Rect(
            fitted.x + fitted.width * region.X / atlas.Width,
            fitted.y + fitted.height * region.Y / atlas.Height,
            fitted.width * region.Width / atlas.Width,
            fitted.height * region.Height / atlas.Height);
        DrawBorder(regionRect, EditorStyles.selectionRect.normal.backgroundColor);
    }


    private static AtlasPreviewData? GetAtlas(DefaultAsset asset)
    {
        var path = SourcePath(asset);
        if (!EditorHostServices.FileSystem.FileExists(path)) return null;
        var stamp = FileStamp.Read(path);
        lock (CacheGate)
        {
            if (AtlasCache.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Data;
        }

        AtlasPreviewData? data;
        try
        {
            var atlas = TextureAtlas.Load(path);
            data = new AtlasPreviewData(atlas);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or ArgumentException)
        {
            data = null;
        }
        lock (CacheGate) AtlasCache[path] = new CachedAtlas(stamp, data);
        return data;
    }


    private static AssetPreviewImage? GetAtlasImage(TextureAtlas atlas, string contextPath)
    {
        if (string.IsNullOrWhiteSpace(atlas.Texture)) return null;
        return GetImage(ResolveReference(atlas.Texture, contextPath));
    }


    private static AssetPreviewImage? GetSpriteImage(Sprite sprite)
    {
        if (TextureAtlasResolver.TryGetPackedAtlas(sprite, out var atlasPath, out var atlas, out _))
            return GetAtlasImage(atlas, ResolvePath(atlasPath));
        if (string.IsNullOrWhiteSpace(sprite.Texture)) return null;
        return GetImage(ResolveReference(sprite.Texture, sprite.sourcePath));
    }


    private static string AtlasInfo(DefaultAsset asset)
    {
        var data = GetAtlas(asset)?.Atlas;
        return data is null ? "Texture Atlas | Preview unavailable" :
            $"{data.Width} x {data.Height} | {data.Sprites.Count} sprites";
    }


    private static bool IsAtlas(DefaultAsset asset) =>
        asset.assetType.Equals(nameof(TextureAtlas), StringComparison.OrdinalIgnoreCase) ||
        SourcePath(asset).EndsWith(".atlas.yaml", StringComparison.OrdinalIgnoreCase);


    private readonly record struct CachedAtlas(FileStamp Stamp, AtlasPreviewData? Data);
    private readonly record struct AtlasPreviewData(TextureAtlas Atlas);
}
