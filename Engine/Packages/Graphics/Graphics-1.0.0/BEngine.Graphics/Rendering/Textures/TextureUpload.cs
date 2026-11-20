using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public static class TextureUpload
{
    public static long GetUploadByteSize(IGraphicsDevice device, int width, int height, TextureCompressionFormat format, int mipCount)
    {
        var gpuFormat = ToGraphicsFormat(format);
        var compressed = gpuFormat != GraphicsTextureFormat.Rgba8Unorm;
        var actual = compressed && device is IGraphicsTextureFormatSupport support && support.SupportsTextureFormat(gpuFormat)
            ? format : TextureCompressionFormat.Rgba32;
        long bytes = 0;
        for (var level = 0; level < mipCount; level++) bytes += TextureImageData.LevelSize(Math.Max(1, width >> level), Math.Max(1, height >> level), actual);
        return bytes;
    }
    private static GraphicsTextureFormat ToGraphicsFormat(TextureCompressionFormat format) => format switch
    {
        TextureCompressionFormat.Bc1 => GraphicsTextureFormat.Bc1RgbaUnorm,
        TextureCompressionFormat.Bc3 => GraphicsTextureFormat.Bc3RgbaUnorm,
        TextureCompressionFormat.Bc7 => GraphicsTextureFormat.Bc7RgbaUnorm,
        TextureCompressionFormat.Etc2Rgba8 => GraphicsTextureFormat.Etc2Rgba8Unorm,
        TextureCompressionFormat.Astc4x4 => GraphicsTextureFormat.Astc4x4Unorm,
        _ => GraphicsTextureFormat.Rgba8Unorm
    };
    /// <summary>Uploads stored blocks when supported; otherwise decodes every mip into RGBA8.</summary>
    public static IGraphicsTexture2D Create(IGraphicsDevice device, string label, ReadOnlySpan<byte> bytes,
        TextureFilterMode filter = TextureFilterMode.Bilinear, TextureWrapMode wrap = TextureWrapMode.Clamp)
    {
        return Create(device, label, TextureImageData.Decode(bytes), filter, wrap);
    }

    public static IGraphicsTexture2D Create(IGraphicsDevice device, string label, TextureImageData image,
        TextureFilterMode filter = TextureFilterMode.Bilinear, TextureWrapMode wrap = TextureWrapMode.Clamp)
    {
        return CreatePrepared(device, label, Prepare(image, SupportsStoredFormat(device, image.Format)), filter, wrap);
    }

    internal static bool SupportsStoredFormat(IGraphicsDevice device, TextureCompressionFormat compression)
    {
        var format = ToGraphicsFormat(compression);
        return format == GraphicsTextureFormat.Rgba8Unorm ||
            device is IGraphicsTextureFormatSupport support && support.SupportsTextureFormat(format);
    }

    // Pure CPU preparation: no graphics object or runtime service is touched.
    internal static PreparedTexture Prepare(TextureImageData image, bool supportsStoredFormat)
    {
        var format = ToGraphicsFormat(image.Format);
        byte[] upload;
        if (format != GraphicsTextureFormat.Rgba8Unorm && !supportsStoredFormat)
        {
            // Decoder output belongs to this upload. Avoid passing it through
            // the public constructor's defensive copy, then copying it again
            // to pack the mip chain. A single mip needs no staging copy at all.
            if (image.MipLevels.Count == 1)
            {
                upload = image.DecodeRgba();
                if (upload.Length != TextureImageData.LevelSize(image.Width, image.Height, TextureCompressionFormat.Rgba32))
                    throw new InvalidDataException("Texture mip 0 has an invalid byte length.");
            }
            else
            {
                var byteCount = 0;
                for (var level = 0; level < image.MipLevels.Count; level++)
                    byteCount = checked(byteCount + TextureImageData.LevelSize(Math.Max(1, image.Width >> level),
                        Math.Max(1, image.Height >> level), TextureCompressionFormat.Rgba32));
                upload = new byte[byteCount];
                var offset = 0;
                for (var level = 0; level < image.MipLevels.Count; level++)
                {
                    var pixels = image.DecodeRgba(level);
                    var length = TextureImageData.LevelSize(Math.Max(1, image.Width >> level),
                        Math.Max(1, image.Height >> level), TextureCompressionFormat.Rgba32);
                    if (pixels.Length != length) throw new InvalidDataException($"Texture mip {level} has an invalid byte length.");
                    pixels.CopyTo(upload.AsSpan(offset, length));
                    offset += length;
                }
            }
            format = GraphicsTextureFormat.Rgba8Unorm;
        }
        // A single stored level is already tightly packed. The device consumes
        // this read-only span during creation, so no staging clone is needed.
        else upload = image.MipLevels.Count == 1 ? image.MipLevels[0] : image.PackLevels();
        return new(image.Width, image.Height, format, image.MipLevels.Count, upload);
    }

    internal readonly record struct PreparedTexture(int Width, int Height, GraphicsTextureFormat Format, int MipCount, byte[] Bytes);

    internal static IGraphicsTexture2D CreatePrepared(IGraphicsDevice device, string label, PreparedTexture image,
        TextureFilterMode filter, TextureWrapMode wrap)
    {
        var sampling = filter == TextureFilterMode.Point ? GraphicsTextureFilter.Nearest : GraphicsTextureFilter.Linear;
        var address = wrap switch
        {
            TextureWrapMode.Repeat => GraphicsTextureAddressMode.Repeat,
            TextureWrapMode.Mirror => GraphicsTextureAddressMode.MirroredRepeat,
            _ => GraphicsTextureAddressMode.ClampToEdge
        };
        return device.CreateTexture2D(label, new GraphicsTextureDescription(image.Width, image.Height, image.Format,
            GraphicsTextureUsage.Sampled, sampling, sampling, address, image.MipCount,
            filter == TextureFilterMode.Trilinear), image.Bytes);
    }
}
