namespace BEngine.Editor;

public sealed class TextureImporter : AssetImporter
{
    public TextureCompressionFormat compressionFormat { get; set; } = TextureCompressionFormat.Automatic;
    public TextureFilterMode filterMode { get; set; } = TextureFilterMode.Bilinear;
    public TextureWrapMode wrapMode { get; set; } = TextureWrapMode.Clamp;
    public bool generateMipMaps { get; set; }
    public int maxTextureSize { get; set; } = 2048;
    public int pixelsPerUnit { get; set; } = 100;

    protected override void ReadSettings(IReadOnlyDictionary<string, string> settings)
    {
        base.ReadSettings(settings);
        compressionFormat = Get(settings, nameof(compressionFormat), TextureCompressionFormat.Automatic);
        filterMode = Get(settings, nameof(filterMode), TextureFilterMode.Bilinear);
        wrapMode = Get(settings, nameof(wrapMode), TextureWrapMode.Clamp);
        generateMipMaps = Get(settings, nameof(generateMipMaps), false);
        maxTextureSize = NormalizeMaxSize(Get(settings, nameof(maxTextureSize), 2048));
        pixelsPerUnit = Math.Clamp(Get(settings, nameof(pixelsPerUnit), 100), 1, 10000);
    }

    protected override void WriteSettings(IDictionary<string, string> settings)
    {
        base.WriteSettings(settings);
        Set(settings, nameof(compressionFormat), compressionFormat);
        Set(settings, nameof(filterMode), filterMode);
        Set(settings, nameof(wrapMode), wrapMode);
        Set(settings, nameof(generateMipMaps), generateMipMaps);
        Set(settings, nameof(maxTextureSize), NormalizeMaxSize(maxTextureSize));
        Set(settings, nameof(pixelsPerUnit), Math.Clamp(pixelsPerUnit, 1, 10000));
    }

    internal void ApplyTo(Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
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
