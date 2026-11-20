using BEngine.Build;

namespace BEngine.Editor;

[Serializable]
public sealed class TextureImporterPlatformSettings
{
    public BuildTargetPlatform platform { get; set; }
    public bool overridden { get; set; }
    public int maxTextureSize { get; set; } = 2048;
    public TextureCompressionFormat format { get; set; } = TextureCompressionFormat.Automatic;
    public bool generateMipMaps { get; set; }
}
