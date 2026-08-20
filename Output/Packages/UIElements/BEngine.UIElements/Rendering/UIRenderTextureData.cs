using System.Buffers.Binary;
using System.IO.Compression;
using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public readonly record struct UIRenderTextureData(
    int Width,
    int Height,
    GraphicsTextureFormat Format,
    ReadOnlyMemory<byte> Pixels)
{
    internal void Validate()
    {
        if (Width <= 0) throw new ArgumentOutOfRangeException(nameof(Width));
        if (Height <= 0) throw new ArgumentOutOfRangeException(nameof(Height));
        var bytesPerPixel = Format switch
        {
            GraphicsTextureFormat.R8Unorm => 1,
            GraphicsTextureFormat.Rgba8Unorm => 4,
            _ => throw new NotSupportedException($"UI textures cannot use {Format}.")
        };
        var expected = checked(Width * Height * bytesPerPixel);
        if (Pixels.Length != expected)
            throw new ArgumentException($"UI texture requires {expected} bytes, got {Pixels.Length}.", nameof(Pixels));
    }
}
