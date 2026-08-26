namespace BEngine;

[EditorIcon("Icons/Assets/AssetImage.png")]
public sealed class Texture : FileAsset
{
    public int width { get; internal set; }
    public int height { get; internal set; }
    public TextureCompressionFormat compressionFormat { get; internal set; } = TextureCompressionFormat.Automatic;
    public TextureFilterMode filterMode { get; internal set; } = TextureFilterMode.Bilinear;
    public TextureWrapMode wrapMode { get; internal set; } = TextureWrapMode.Clamp;
    public bool mipMaps { get; internal set; }
    public int maxSize { get; internal set; } = 2048;
    public int pixelsPerUnit { get; internal set; } = 100;

    internal Texture() { }
}

public enum TextureCompressionFormat
{
    Automatic,
    Rgba32,
    Bc1,
    Bc3,
    Bc7,
    Etc2Rgba8,
    Astc4x4
}

public enum TextureFilterMode
{
    Point,
    Bilinear,
    Trilinear
}

public enum TextureWrapMode
{
    Clamp,
    Repeat,
    Mirror
}
