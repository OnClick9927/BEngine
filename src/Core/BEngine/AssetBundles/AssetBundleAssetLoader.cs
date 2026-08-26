using System.Globalization;
using BEngine.Rendering;

namespace BEngine.AssetBundles;

/// <summary>Creates runtime assets directly from an active bundle catalog and its verified payloads.</summary>
public static class AssetBundleAssetLoader
{
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
            !textureType.Equals("Sprite", StringComparison.OrdinalIgnoreCase)) return null;

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
        sprite.BindAssetReference(canonical, asset.Guid);
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
}
