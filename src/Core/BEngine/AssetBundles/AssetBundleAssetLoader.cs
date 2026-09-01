using System.Globalization;
using BEngine.Rendering;

namespace BEngine.AssetBundles;

/// <summary>Creates runtime assets directly from an active bundle catalog and its verified payloads.</summary>
public static class AssetBundleAssetLoader
{
    public static Texture? LoadTexture(IAssetBundleManager manager, string reference)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        if (!manager.IsInitialized || manager.ActiveCatalog is not { } catalog) return null;

        var canonical = AssetBundleValidation.NormalizeAddress(reference);
        var asset = catalog.Assets.FirstOrDefault(candidate =>
            candidate.LocalIdentifier > 0 &&
            candidate.Address.Equals(canonical, StringComparison.OrdinalIgnoreCase));
        if (asset is null || asset.OwnerGuid == Guid.Empty ||
            !asset.AssetType.Equals(nameof(Texture), StringComparison.OrdinalIgnoreCase)) return null;

        var expectedAddress = AssetBundleValidation.CreateSubAssetAddress(
            asset.OwnerGuid, asset.LocalIdentifier);
        if (!asset.Address.Equals(expectedAddress, StringComparison.Ordinal) ||
            !Texture.IsSupportedSourcePath(asset.Entry)) return null;
        if (!manager.TryLoadBytes(canonical, out var bytes)) return null;
        if (!PngImageCodec.TryDecode(bytes, out var width, out var height, out _))
            throw new InvalidDataException(
                $"Bundled Texture sub-asset '{canonical}' is not a supported PNG image.");

        var owner = catalog.Assets.FirstOrDefault(candidate =>
            candidate.LocalIdentifier == 0 &&
            (candidate.OwnerGuid == Guid.Empty ? candidate.Guid : candidate.OwnerGuid) == asset.OwnerGuid);
        if (owner is null) return null;

        var texture = new Texture
        {
            name = Path.GetFileNameWithoutExtension(owner.Address),
            width = width,
            height = height,
            sRGB = ReadBoolean(asset.ImporterSettings, "sRGBTexture", true),
            alphaIsTransparency = ReadBoolean(asset.ImporterSettings, "alphaIsTransparency", true),
            isReadable = ReadBoolean(asset.ImporterSettings, "isReadable", false),
            compressionFormat = ReadEnum(asset.ImporterSettings, "compressionFormat",
                TextureCompressionFormat.Automatic),
            filterMode = ReadEnum(asset.ImporterSettings, "filterMode", TextureFilterMode.Bilinear),
            wrapMode = ReadEnum(asset.ImporterSettings, "wrapMode", TextureWrapMode.Clamp),
            mipMaps = ReadBoolean(asset.ImporterSettings, "generateMipMaps", false),
            maxSize = NormalizeMaxSize(ReadInteger(asset.ImporterSettings, "maxTextureSize", 2048)),
            pixelsPerUnit = Math.Clamp(ReadInteger(asset.ImporterSettings, "pixelsPerUnit", 100), 1, 10000)
        };
        texture.BindAssetFile(owner.Address, string.Empty, string.Empty, asset.Guid, asset.AssetType);
        texture.BindSubAssetReference(owner.Address, asset.OwnerGuid, asset.LocalIdentifier, asset.Guid);
        return texture;
    }

    public static Sprite? LoadSprite(IAssetBundleManager manager, string address)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        if (!manager.IsInitialized || manager.ActiveCatalog is null) return null;

        var canonical = AssetBundleValidation.NormalizeAddress(address);
        var asset = manager.ActiveCatalog.Assets.FirstOrDefault(candidate =>
            candidate.Address.Equals(canonical, StringComparison.OrdinalIgnoreCase));
        if (asset is null ||
            !asset.Importer.Equals("TextureImporter", StringComparison.OrdinalIgnoreCase) ||
            !asset.ImporterSettings.TryGetValue("textureType", out var textureType) ||
            !textureType.Equals(nameof(Sprite), StringComparison.OrdinalIgnoreCase)) return null;

        if (!manager.TryLoadBytes(canonical, out var bytes)) return null;
        if (!Path.GetExtension(canonical).Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            !PngImageCodec.TryDecode(bytes, out _, out _, out _))
            throw new InvalidDataException(
                $"Bundled Sprite '{canonical}' is not a supported PNG image.");

        var sprite = Sprite.FromTexture(AssetBundleResourceProvider.ToVirtualPath(canonical),
            new Vector2(
                (Fix64)(double)ReadPivot(asset.ImporterSettings, "spritePivotX"),
                (Fix64)(double)ReadPivot(asset.ImporterSettings, "spritePivotY")),
            canonical);
        sprite.Id = asset.Guid;
        sprite.BindSourceIdentity(asset.Guid.ToString("N"), 21300000, canonical);
        sprite.sourcePath = string.Empty;
        sprite.assetType = nameof(Sprite);
        return sprite;
    }

    private static float ReadPivot(IReadOnlyDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var value) &&
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pivot) &&
        float.IsFinite(pivot)
            ? Math.Clamp(pivot, 0, 1)
            : 0.5f;

    private static bool ReadBoolean(
        IReadOnlyDictionary<string, string> settings,
        string key,
        bool fallback) =>
        settings.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int ReadInteger(
        IReadOnlyDictionary<string, string> settings,
        string key,
        int fallback) =>
        settings.TryGetValue(key, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static TEnum ReadEnum<TEnum>(
        IReadOnlyDictionary<string, string> settings,
        string key,
        TEnum fallback) where TEnum : struct, Enum =>
        settings.TryGetValue(key, out var value) &&
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : fallback;

    private static int NormalizeMaxSize(int value)
    {
        var clamped = Math.Clamp(value, 32, 16384);
        var lower = 32;
        while (lower <= clamped / 2) lower *= 2;
        var upper = Math.Min(16384, lower * 2);
        return clamped - lower < upper - clamped ? lower : upper;
    }
}
