using System.Globalization;
using BEngine.Platform;
using BEngine.Rendering;
using BEngine.Serialization;
namespace BEngine;
internal static class GraphicsAssetLoading
{
    private static FileUtility FileSystem => RuntimeServices.GetRequiredService<global::BEngine.Platform.FileUtility>();
    private static PathUtility Paths => RuntimeServices.GetRequiredService<global::BEngine.Platform.PathUtility>();
    private static bool TryReadMeta(string path, out AssetReferenceMetadata meta) => RuntimeAssetReferences.TryReadMetadata(path, out meta);
    internal static BAsset? LoadFile(string fullPath, Type assetType)
    {
        if (assetType == typeof(Texture))
        {
            if (!Texture.IsSupportedSourcePath(fullPath)) return null;
            var texture = new Texture { name = Paths.GetFileName(fullPath) };
            if (!TryReadImageSize(fullPath, out var width, out var height)) return null;
            texture.width = width;
            texture.height = height;
            ApplyTextureImportSettings(texture, ReadTextureImportSettings(fullPath));
            return texture;
        }
        if (assetType == typeof(Font)) return new Font { name = Paths.GetFileName(fullPath) };
        if (assetType == typeof(Shader))
            return new Shader(Paths.GetFileNameWithoutExtension(fullPath), FileSystem.ReadAllText(fullPath));
        return null;
    }
    internal static (TextureFilterMode FilterMode, TextureWrapMode WrapMode) ReadTextureSamplingSettings(
        string fullPath)
    {
        var settings = ReadTextureImportSettings(fullPath);
        return (settings.FilterMode, settings.WrapMode);
    }

    private static TextureImportSettings ReadTextureImportSettings(string fullPath)
    {
        if (!TryReadMeta(fullPath, out var meta)) return TextureImportSettings.Default;
        return ReadTextureImportSettings(meta.Settings);
    }

    internal static void ApplyTextureImportSettings(Texture texture, IReadOnlyDictionary<string, string> settings) =>
        ApplyTextureImportSettings(texture, ReadTextureImportSettings(settings));

    private static TextureImportSettings ReadTextureImportSettings(IReadOnlyDictionary<string, string> settings)
    {
        return new TextureImportSettings(
            ReadBoolean(settings, "sRGBTexture", true),
            ReadBoolean(settings, "alphaIsTransparency", true),
            ReadBoolean(settings, "isReadable", false),
            ReadEnum(settings, "compressionFormat", TextureCompressionFormat.Automatic),
            ReadEnum(settings, "filterMode", TextureFilterMode.Bilinear),
            ReadEnum(settings, "wrapMode", TextureWrapMode.Clamp),
            ReadBoolean(settings, "generateMipMaps", false),
            NormalizeMaxSize(ReadInteger(settings, "maxTextureSize", 2048)),
            Math.Clamp(ReadInteger(settings, "pixelsPerUnit", 100), 1, 10000));
    }

    private static void ApplyTextureImportSettings(Texture texture, TextureImportSettings settings)
    {
        texture.sRGB = settings.SRgbTexture;
        texture.alphaIsTransparency = settings.AlphaIsTransparency;
        texture.isReadable = settings.IsReadable;
        texture.compressionFormat = settings.CompressionFormat;
        texture.filterMode = settings.FilterMode;
        texture.wrapMode = settings.WrapMode;
        texture.mipMaps = settings.GenerateMipMaps;
        texture.maxSize = settings.MaxTextureSize;
        texture.pixelsPerUnit = settings.PixelsPerUnit;
    }

    private static bool ReadBoolean(IReadOnlyDictionary<string, string> settings, string key, bool fallback) =>
        settings.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int ReadInteger(IReadOnlyDictionary<string, string> settings, string key, int fallback) =>
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

    private static bool TryReadImageSize(string path, out int width, out int height) =>
        Rendering.ImageCodec.TryGetInfo(FileSystem.ReadAllBytes(path), out width, out height);

    private readonly record struct TextureImportSettings(
        bool SRgbTexture,
        bool AlphaIsTransparency,
        bool IsReadable,
        TextureCompressionFormat CompressionFormat,
        TextureFilterMode FilterMode,
        TextureWrapMode WrapMode,
        bool GenerateMipMaps,
        int MaxTextureSize,
        int PixelsPerUnit)
    {
        internal static TextureImportSettings Default => new(
            true,
            true,
            false,
            TextureCompressionFormat.Automatic,
            TextureFilterMode.Bilinear,
            TextureWrapMode.Clamp,
            false,
            2048,
            100);
    }
}
