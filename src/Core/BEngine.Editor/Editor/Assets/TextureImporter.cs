namespace BEngine.Editor;

public sealed class TextureImporter : AssetImporter
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
    public float spritePivotX { get; set; } = 0.5f;
    public float spritePivotY { get; set; } = 0.5f;

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
        spritePivotX = Math.Clamp(Get(settings, nameof(spritePivotX), 0.5f), 0, 1);
        spritePivotY = Math.Clamp(Get(settings, nameof(spritePivotY), 0.5f), 0, 1);
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
        Set(settings, nameof(spritePivotX), Math.Clamp(spritePivotX, 0, 1));
        Set(settings, nameof(spritePivotY), Math.Clamp(spritePivotY, 0, 1));
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

    private static int NormalizeMaxSize(int value)
    {
        var clamped = Math.Clamp(value, 32, 16384);
        var lower = 32;
        while (lower <= clamped / 2) lower *= 2;
        var upper = Math.Min(16384, lower * 2);
        return clamped - lower < upper - clamped ? lower : upper;
    }
}

public enum TextureImporterType
{
    Texture,
    Sprite,
    NormalMap,
    Cursor,
    EditorGui
}
