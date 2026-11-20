using System.Globalization;
using BEngine.Platform;
namespace BEngine;
internal sealed class SpriteReferenceCodec : RuntimeObjectCodec
{
    private static FileUtility FileSystem => RuntimeServices.GetRequiredService<global::BEngine.Platform.FileUtility>();
    private static long _cacheGeneration = -1;
    private static FileUtility? _cacheFiles;
    public override Type ObjectType => typeof(Sprite);
    public override void ClearReferences() { Clear(); SpriteSourceRegistry.Invalidate(); }
    public override BObject? ReadReference(string reference, IReadOnlyDictionary<string,string>? legacyFields = null) => SpriteSourceRegistry.LoadSpriteReference(reference, legacyFields);
    public override string WriteReference(BObject value) => value is Sprite sprite && !string.IsNullOrWhiteSpace(sprite.OwnerGuid) && sprite.LocalIdentifier > 0
        ? $"guid:{sprite.OwnerGuid}#subasset={sprite.LocalIdentifier.ToString(CultureInfo.InvariantCulture)}" : ((Sprite)value).assetPath;
    internal static Sprite? Load(string reference)
    {
        if (!RuntimeAssetReferences.TryParseSubAssetReference(reference, out var path, out var id)) return null;
        return LoadSpriteSubAsset(path,id);
    }
    private static string CanonicalPath(string path) => RuntimeAssetReferences.CanonicalPath(path);
    private static string? ResolveGuidPath(Guid guid) => RuntimeAssetReferences.ResolveGuidPath(guid);
    private static AssetReferenceMetadata? ReadSubAssetMeta(string path) => RuntimeAssetReferences.ReadSubAssetMetadata(path);
    private static BAsset? Load(string path, Type type) => BAsset.Load(path,type);
    private static readonly Dictionary<SubAssetCacheKey,WeakReference<Sprite>> SpriteSubAssetCache = [];
    internal static void Clear() => SpriteSubAssetCache.Clear();
    private readonly record struct SubAssetCacheKey(string Path,long LocalIdentifier,Type AssetType);
    private static bool TryGetTarget(SubAssetCacheKey key, out Sprite sprite)
    {
        if (SpriteSubAssetCache.TryGetValue(key, out var reference) && reference.TryGetTarget(out sprite!) && !sprite.isDestroyed) return true;
        sprite = null!;
        return false;
    }
    private static Sprite? LoadSpriteSubAsset(string path, long localIdentifier)
    {
        if (_cacheGeneration != BAsset.cacheGeneration || !ReferenceEquals(_cacheFiles,FileSystem))
        { Clear(); _cacheGeneration=BAsset.cacheGeneration; _cacheFiles=FileSystem; }
        if (localIdentifier <= 0 || string.IsNullOrWhiteSpace(path)) return null;
        string mainFullPath;
        if (path.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(path.AsSpan("guid:".Length), out var ownerGuid) ||
                ResolveGuidPath(ownerGuid) is not { } resolvedPath)
                return LoadBundledSprite(ownerGuid, localIdentifier);
            mainFullPath = resolvedPath;
        }
        else mainFullPath = RuntimeAssetReferences.ResolvePath(path);
        if (!FileSystem.FileExists(mainFullPath))
            return path.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) &&
                   Guid.TryParse(path.AsSpan("guid:".Length), out var missingOwnerGuid)
                ? LoadBundledSprite(missingOwnerGuid, localIdentifier)
                : null;

        var mainMeta = ReadSubAssetMeta(mainFullPath + ".meta");
        if (mainMeta is null || !Guid.TryParse(mainMeta.Guid, out var parentGuid) ||
            !(localIdentifier == 21300000 && mainMeta.Settings.TryGetValue("textureType", out var type) && type.Equals(nameof(Sprite), StringComparison.OrdinalIgnoreCase))) return null;
        var cacheKey = new SubAssetCacheKey(CanonicalPath(mainFullPath), localIdentifier, typeof(Sprite));
        if (TryGetTarget(cacheKey, out var cached)) return cached;

        if (Load(mainFullPath, typeof(Texture)) is not Texture texture) return null;
        var pivot = new Vector2(
            ReadPivot(mainMeta.Settings, "spritePivotX"),
            ReadPivot(mainMeta.Settings, "spritePivotY"));
        var sprite = texture.CreateSprite(pivot, localIdentifier);
        sprite.border = new(ReadBorder("spriteBorderLeft"), ReadBorder("spriteBorderBottom"),
            ReadBorder("spriteBorderRight"), ReadBorder("spriteBorderTop"));
        sprite.BindSourceIdentity(parentGuid.ToString("N"), localIdentifier,
            RuntimeAssetReferences.ToReferencePath(mainFullPath));
        if (TryGetTarget(cacheKey, out var existing)) return existing;
        SpriteSubAssetCache[cacheKey] = new WeakReference<Sprite>(sprite);
        return sprite;

        Fix64 ReadBorder(string key) => mainMeta.Settings.TryGetValue(key, out var text) &&
            Fix64.TryParse(text, CultureInfo.InvariantCulture, out var value)
            ? Fix64.Max(0, value) : Fix64.Zero;
    }

    private static Sprite? LoadBundledSprite(Guid ownerGuid, long localIdentifier)
    {
        if (ownerGuid == Guid.Empty || localIdentifier <= 0) return null;
        var reference = $"guid:{ownerGuid:N}#subasset={localIdentifier}";
        try { return Resources.Load(reference, typeof(Sprite)) as Sprite; }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or
                                          ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private static Fix64 ReadPivot(IReadOnlyDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var value) &&
        Fix64.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? Mathf.Clamp01(parsed)
            : Fix64.Half;

}
