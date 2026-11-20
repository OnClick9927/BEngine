using BEngine.Rendering;

namespace BEngine.Editor;

[AssetImporterVersion(4)]
public sealed partial class TextureImporter : AssetImporter
{
    public TextureImporterType textureType { get; set; } = TextureImporterType.Texture;
    public bool sRGBTexture { get; set; } = true;
    public bool alphaIsTransparency { get; set; } = true;
    public bool isReadable { get; set; }
    public TextureCompressionFormat compressionFormat { get; set; } = TextureCompressionFormat.Automatic;
    public TextureFilterMode filterMode { get; set; } = TextureFilterMode.Bilinear;
    public TextureWrapMode wrapMode { get; set; } = TextureWrapMode.Clamp;
    public bool generateMipMaps { get; set; }
    public int maxTextureSize { get; set; } = 2048;
    public int pixelsPerUnit { get; set; } = 100;
    public Fix64 spritePivotX { get; set; } = Fix64.Half;
    public Fix64 spritePivotY { get; set; } = Fix64.Half;
    public Fix64 spriteBorderLeft { get; set; }
    public Fix64 spriteBorderBottom { get; set; }
    public Fix64 spriteBorderRight { get; set; }
    public Fix64 spriteBorderTop { get; set; }
    public List<TextureImporterPlatformSettings> platformSettings { get; set; } = [];

    public override void Import(AssetImportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var source = context.ReadSourceBytes();
        if (!ImageCodec.TryDecode(source, out var width, out var height, out var pixels))
            throw new InvalidDataException($"Texture '{context.assetPath}' is not a supported image.");
        var limit = NormalizeMaxSize(maxTextureSize);
        if (width > limit || height > limit)
        {
            var scale = (double)limit / Math.Max(width, height);
            var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
            var targetHeight = Math.Max(1, (int)Math.Round(height * scale));
            pixels = TexturePixelProcessing.ResizeRgba(pixels, width, height, targetWidth, targetHeight,
                filterMode == TextureFilterMode.Point, sRGBTexture);
            width = targetWidth; height = targetHeight;
        }
        context.cancellationToken.ThrowIfCancellationRequested();
        context.WriteArtifact(TextureEncoder.Encode(width, height, pixels, compressionFormat,
            generateMipMaps, sRGBTexture, context.cancellationToken));
    }

    protected override void ReadSettings(IReadOnlyDictionary<string, string> settings)
    {
        base.ReadSettings(settings);
        textureType = Get(settings, nameof(textureType), TextureImporterType.Texture);
        sRGBTexture = Get(settings, nameof(sRGBTexture), true);
        alphaIsTransparency = Get(settings, nameof(alphaIsTransparency), true);
        isReadable = Get(settings, nameof(isReadable), false);
        compressionFormat = Get(settings, nameof(compressionFormat), TextureCompressionFormat.Automatic);
        filterMode = Get(settings, nameof(filterMode), TextureFilterMode.Bilinear);
        wrapMode = Get(settings, nameof(wrapMode), TextureWrapMode.Clamp);
        generateMipMaps = Get(settings, nameof(generateMipMaps), false);
        maxTextureSize = NormalizeMaxSize(Get(settings, nameof(maxTextureSize), 2048));
        pixelsPerUnit = Math.Clamp(Get(settings, nameof(pixelsPerUnit), 100), 1, 10000);
        spritePivotX = Mathf.Clamp01(ReadFixed(settings, nameof(spritePivotX), Fix64.Half));
        spritePivotY = Mathf.Clamp01(ReadFixed(settings, nameof(spritePivotY), Fix64.Half));
        spriteBorderLeft = Fix64.Max(0, ReadFixed(settings, nameof(spriteBorderLeft)));
        spriteBorderBottom = Fix64.Max(0, ReadFixed(settings, nameof(spriteBorderBottom)));
        spriteBorderRight = Fix64.Max(0, ReadFixed(settings, nameof(spriteBorderRight)));
        spriteBorderTop = Fix64.Max(0, ReadFixed(settings, nameof(spriteBorderTop)));
        ReadPlatformSettings(settings);
    }

    protected override void WriteSettings(IDictionary<string, string> settings)
    {
        base.WriteSettings(settings);
        Set(settings, nameof(textureType), textureType);
        Set(settings, nameof(sRGBTexture), sRGBTexture);
        Set(settings, nameof(alphaIsTransparency), alphaIsTransparency);
        Set(settings, nameof(isReadable), isReadable);
        Set(settings, nameof(compressionFormat), compressionFormat);
        Set(settings, nameof(filterMode), filterMode);
        Set(settings, nameof(wrapMode), wrapMode);
        Set(settings, nameof(generateMipMaps), generateMipMaps);
        Set(settings, nameof(maxTextureSize), NormalizeMaxSize(maxTextureSize));
        Set(settings, nameof(pixelsPerUnit), Math.Clamp(pixelsPerUnit, 1, 10000));
        Set(settings, nameof(spritePivotX), Mathf.Clamp01(spritePivotX));
        Set(settings, nameof(spritePivotY), Mathf.Clamp01(spritePivotY));
        Set(settings, nameof(spriteBorderLeft), Fix64.Max(0, spriteBorderLeft));
        Set(settings, nameof(spriteBorderBottom), Fix64.Max(0, spriteBorderBottom));
        Set(settings, nameof(spriteBorderRight), Fix64.Max(0, spriteBorderRight));
        Set(settings, nameof(spriteBorderTop), Fix64.Max(0, spriteBorderTop));
        WritePlatformSettings(settings);
    }

    internal void ApplyTo(Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        texture.sRGB = sRGBTexture;
        texture.alphaIsTransparency = alphaIsTransparency;
        texture.isReadable = isReadable;
        texture.compressionFormat = compressionFormat;
        texture.filterMode = filterMode;
        texture.wrapMode = wrapMode;
        texture.mipMaps = generateMipMaps;
        texture.maxSize = NormalizeMaxSize(maxTextureSize);
        texture.pixelsPerUnit = Math.Clamp(pixelsPerUnit, 1, 10000);
    }

    private static Fix64 ReadFixed(IReadOnlyDictionary<string, string> settings, string key, Fix64 fallback = default) =>
        settings.TryGetValue(key, out var text) && Fix64.TryParse(text, out var parsed) ? parsed : fallback;

    private static int NormalizeMaxSize(int value)
    {
        var clamped = Math.Clamp(value, 32, 16384);
        var lower = 32;
        while (lower <= clamped / 2) lower *= 2;
        var upper = Math.Min(16384, lower * 2);
        return clamped - lower < upper - clamped ? lower : upper;
    }
}
