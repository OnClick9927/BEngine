using System.Buffers.Binary;
using System.IO.Compression;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Rendering;

public readonly record struct GpuCanvasTextureData(
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
            _ => throw new NotSupportedException($"Canvas textures cannot use {Format}.")
        };
        var expected = checked(Width * Height * bytesPerPixel);
        if (Pixels.Length != expected)
            throw new ArgumentException($"Canvas texture requires {expected} bytes, got {Pixels.Length}.");
    }
}
