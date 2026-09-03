using System.Globalization;
using BEngine.Rendering;
using BEngine.Serialization;

namespace BEngine.AssetBundles;

/// <summary>Creates runtime assets directly from an active bundle catalog and its verified payloads.</summary>
public static class AssetBundleAssetLoader
{
    public static Texture? LoadTexture(IAssetBundleManager manager, string reference)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        if (!manager.IsInitialized) return null;

        var canonical = AssetBundleValidation.NormalizeAddress(reference);
        var fallbackCatalog = manager is AssetBundleManager ? null : manager.ActiveCatalog;
        var asset = FindAsset(manager, fallbackCatalog, canonical);
        if (asset is null ||
            !asset.AssetType.Equals(nameof(Texture), StringComparison.OrdinalIgnoreCase)) return null;
        AssetBundleAsset owner;
        if (asset.LocalIdentifier > 0)
        {
            if (asset.OwnerGuid == Guid.Empty ||
                !asset.Address.Equals(AssetBundleValidation.CreateSubAssetAddress(
                    asset.OwnerGuid, asset.LocalIdentifier), StringComparison.Ordinal)) return null;
            owner = FindMainAsset(manager, fallbackCatalog, asset.OwnerGuid)!;
            if (owner is null) return null;
        }
        else owner = asset;
        if (!manager.TryLoadBytes(asset.Address, out var bytes)) return null;
        if (!PngImageCodec.TryDecode(bytes, out var width, out var height, out _))
            throw new InvalidDataException(
                $"Bundled Texture sub-asset '{canonical}' is not a supported PNG image.");

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
        if (asset.LocalIdentifier > 0)
        {
            texture.BindAssetFile(owner.Address, string.Empty, string.Empty, asset.Guid, asset.AssetType);
            texture.BindSubAssetReference(owner.Address, asset.OwnerGuid, asset.LocalIdentifier, asset.Guid);
        }
        else texture.BindAssetFile(asset.Address, string.Empty, string.Empty, asset.Guid, asset.AssetType);
        return texture;
    }

    public static BAsset? LoadAsset(IAssetBundleManager manager, string reference, Type assetType)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentNullException.ThrowIfNull(assetType);
        if (!typeof(BAsset).IsAssignableFrom(assetType) || assetType.IsAbstract)
            throw new ArgumentException($"{assetType.FullName} is not a concrete BAsset type.", nameof(assetType));
        if (!manager.IsInitialized) return null;
        if (assetType == typeof(Texture)) return LoadTexture(manager, reference);

        var canonical = AssetBundleValidation.NormalizeAddress(reference);
        var fallbackCatalog = manager is AssetBundleManager ? null : manager.ActiveCatalog;
        var entry = FindAsset(manager, fallbackCatalog, canonical);
        if (entry is null || entry.LocalIdentifier > 0 ||
            !manager.TryLoadBytes(entry.Address, out var bytes)) return null;
        var virtualPath = AssetBundleResourceProvider.ToVirtualPath(entry.Address);
        var asset = DecodeAsset(bytes, virtualPath, entry.Address, assetType);
        if (asset is null) return null;
        if (!assetType.IsInstanceOfType(asset))
            throw new InvalidDataException(
                $"Bundled asset '{entry.Address}' contains {asset.GetType().FullName}, " +
                $"expected {assetType.FullName}.");
        asset.BindAssetFile(entry.Address, string.Empty, string.Empty, entry.Guid, entry.AssetType);
        if (string.IsNullOrWhiteSpace(asset.name)) asset.name = AssetName(entry.Address);
        if (asset is Scene scene) scene.path = entry.Address;
        return asset;
    }

    public static Sprite? LoadSprite(IAssetBundleManager manager, string address)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        if (!manager.IsInitialized) return null;

        var canonical = AssetBundleValidation.NormalizeAddress(address);
        var fallbackCatalog = manager is AssetBundleManager ? null : manager.ActiveCatalog;
        AssetBundleAsset? asset;
        Guid ownerGuid;
        long localIdentifier;
        if (AssetBundleValidation.TryParseSubAssetAddress(canonical, out ownerGuid, out localIdentifier))
        {
            // TextureImporter exposes its single Sprite as a stable synthetic sub-asset. It does not
            // need a separate catalog payload; the owner Texture contains the image bytes.
            if (localIdentifier != 21300000) return null;
            asset = FindMainAsset(manager, fallbackCatalog, ownerGuid);
        }
        else
        {
            asset = FindAsset(manager, fallbackCatalog, canonical);
            ownerGuid = asset?.OwnerGuid is { } owner && owner != Guid.Empty ? owner : asset?.Guid ?? Guid.Empty;
            localIdentifier = 21300000;
        }
        if (asset is null ||
            !asset.Importer.Equals("TextureImporter", StringComparison.OrdinalIgnoreCase) ||
            !asset.ImporterSettings.TryGetValue("textureType", out var textureType) ||
            !textureType.Equals(nameof(Sprite), StringComparison.OrdinalIgnoreCase)) return null;

        if (!manager.TryLoadBytes(asset.Address, out var bytes)) return null;
        if (!Path.GetExtension(asset.Address).Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            !PngImageCodec.TryDecode(bytes, out _, out _, out _))
            throw new InvalidDataException(
                $"Bundled Sprite '{address}' is not a supported PNG image.");

        var sprite = Sprite.FromTexture(AssetBundleResourceProvider.ToVirtualPath(asset.Address),
            new Vector2(
                (Fix64)(double)ReadPivot(asset.ImporterSettings, "spritePivotX"),
                (Fix64)(double)ReadPivot(asset.ImporterSettings, "spritePivotY")),
            asset.Address);
        sprite.Id = asset.Guid;
        sprite.BindSourceIdentity(ownerGuid.ToString("N"), localIdentifier, asset.Address);
        sprite.sourcePath = string.Empty;
        sprite.assetType = nameof(Sprite);
        return sprite;
    }

    private static BAsset? DecodeAsset(
        byte[] bytes,
        string virtualPath,
        string address,
        Type assetType)
    {
        if (RuntimeAssetCodecRegistry.TryDecode(bytes, virtualPath, assetType, out var decoded)) return decoded;
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        if (assetType == typeof(PrefabAsset)) return PrefabAssetSerialization.Deserialize(text, virtualPath);
        if (assetType == typeof(Material)) return Material.Deserialize(text);
        if (assetType == typeof(TextureAtlas)) return TextureAtlas.Deserialize(text);
        if (assetType == typeof(Scene)) return SceneAssetSerialization.Deserialize(text);
        if (assetType == typeof(TextAsset)) return new TextAsset(text, virtualPath);
        if (assetType == typeof(Shader)) return new Shader(AssetName(address), text);
        if (assetType == typeof(Font)) return new Font { name = AssetName(address) };
        if (address.EndsWith(".asset.yaml", StringComparison.OrdinalIgnoreCase))
        {
            var document = YamlUtility.Deserialize<ManagedAssetData>(text);
            var storedType = BAssetReferenceLoader.ResolveType(document.TypeName) ??
                             throw new InvalidDataException(
                                 $"Managed asset type '{document.TypeName}' is not loaded.");
            if (!assetType.IsAssignableFrom(storedType))
                throw new InvalidDataException(
                    $"Managed asset contains {storedType.FullName}, expected {assetType.FullName}.");
            return YamlUtility.Deserialize(document.Data, storedType) as BAsset;
        }
        return YamlUtility.Deserialize(text, assetType) as BAsset;
    }

    private static AssetBundleAsset? FindAsset(
        IAssetBundleManager manager,
        AssetBundleCatalog? fallbackCatalog,
        string canonicalAddress)
    {
        if (manager is AssetBundleManager runtime)
            return runtime.TryGetActiveAsset(canonicalAddress, out var indexed) ? indexed : null;
        return fallbackCatalog?.Assets.FirstOrDefault(candidate =>
            candidate.Address.Equals(canonicalAddress, StringComparison.OrdinalIgnoreCase));
    }

    private static AssetBundleAsset? FindMainAsset(
        IAssetBundleManager manager,
        AssetBundleCatalog? fallbackCatalog,
        Guid ownerGuid)
    {
        if (manager is AssetBundleManager runtime)
            return runtime.TryGetActiveMainAsset(ownerGuid, out var indexed) ? indexed : null;
        return fallbackCatalog?.Assets.FirstOrDefault(candidate =>
            candidate.LocalIdentifier == 0 &&
            (candidate.OwnerGuid == Guid.Empty ? candidate.Guid : candidate.OwnerGuid) == ownerGuid);
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

    private static string AssetName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".scene", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".material", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".atlas", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name)
            : name;
    }
}
