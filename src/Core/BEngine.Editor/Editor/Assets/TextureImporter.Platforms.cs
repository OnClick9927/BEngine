using BEngine.Build;
using BEngine.Platform;
using BEngine.Rendering;
using System.Security.Cryptography;

namespace BEngine.Editor;

public sealed partial class TextureImporter
{
    private void ReadPlatformSettings(IReadOnlyDictionary<string, string> settings)
    {
        platformSettings = [];
        foreach (var platform in Enum.GetValues<BuildTargetPlatform>())
        {
            var key = $"platform.{platform}.";
            platformSettings.Add(new TextureImporterPlatformSettings
            {
                platform = platform,
                overridden = Get(settings, key + "overridden", false),
                maxTextureSize = NormalizeMaxSize(Get(settings, key + "maxTextureSize", maxTextureSize)),
                format = Get(settings, key + "format", compressionFormat),
                generateMipMaps = Get(settings, key + "generateMipMaps", generateMipMaps)
            });
        }
    }

    private void WritePlatformSettings(IDictionary<string, string> settings)
    {
        var seen = new HashSet<BuildTargetPlatform>();
        foreach (var item in platformSettings)
            if (item is null || !Enum.IsDefined(item.platform) || !seen.Add(item.platform))
                throw new InvalidDataException("Texture platform settings contain an invalid or repeated platform.");
        foreach (var key in settings.Keys.Where(key => key.StartsWith("platform.", StringComparison.Ordinal)).ToArray())
            settings.Remove(key);
        foreach (var item in platformSettings)
        {
            if (!Enum.IsDefined(item.format)) throw new InvalidDataException("Invalid platform texture format.");
            var key = $"platform.{item.platform}.";
            Set(settings, key + "overridden", item.overridden);
            Set(settings, key + "maxTextureSize", NormalizeMaxSize(item.maxTextureSize));
            Set(settings, key + "format", item.format);
            Set(settings, key + "generateMipMaps", item.generateMipMaps);
        }
    }

    /// <summary>Creates target-specific bytes without changing source metadata or the editor artifact.</summary>
    public static bool TryBuildPlatformArtifact(string sourcePath, string expectedSourceHash,
        IReadOnlyDictionary<string, string> settings, BuildTargetPlatform platform,
        FileUtility fileSystem, CancellationToken cancellationToken,
        out byte[] content, out IReadOnlyDictionary<string, string> effectiveSettings)
    {
        content = []; effectiveSettings = settings;
        if (!Enum.IsDefined(platform)) throw new ArgumentOutOfRangeException(nameof(platform));
        var key = $"platform.{platform}.";
        if (!Get(settings, key + "overridden", false)) return false;
        cancellationToken.ThrowIfCancellationRequested();
        var source = fileSystem.ReadAllBytes(sourcePath);
        if (!Convert.ToHexString(SHA256.HashData(source)).Equals(expectedSourceHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Texture source changed after the asset snapshot: {sourcePath}");
        if (!ImageCodec.TryDecode(source, out var width, out var height, out var pixels))
            throw new InvalidDataException($"Texture '{sourcePath}' cannot be decoded for {platform}.");
        var limit = NormalizeMaxSize(Get(settings, key + "maxTextureSize", Get(settings, "maxTextureSize", 2048)));
        var format = Get(settings, key + "format", Get(settings, "compressionFormat", TextureCompressionFormat.Automatic));
        var mips = Get(settings, key + "generateMipMaps", Get(settings, "generateMipMaps", false));
        var sRgb = Get(settings, "sRGBTexture", true);
        if (width > limit || height > limit)
        {
            var scale = (double)limit / Math.Max(width, height);
            var nextWidth = Math.Max(1, (int)Math.Round(width * scale));
            var nextHeight = Math.Max(1, (int)Math.Round(height * scale));
            pixels = TexturePixelProcessing.ResizeRgba(pixels, width, height, nextWidth, nextHeight,
                Get(settings, "filterMode", TextureFilterMode.Bilinear) == TextureFilterMode.Point, sRgb);
            width = nextWidth; height = nextHeight;
        }
        content = TextureEncoder.Encode(width, height, pixels, format, mips, sRgb, cancellationToken);
        var effective = new Dictionary<string, string>(settings, StringComparer.Ordinal);
        Set(effective, "maxTextureSize", limit); Set(effective, "compressionFormat", format);
        Set(effective, "generateMipMaps", mips); Set(effective, "builtForPlatform", platform);
        effectiveSettings = effective;
        return true;
    }
}
