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
        TextureImageData image;
        if (TextureImageData.IsContainer(bytes)) image = TextureImageData.Deserialize(bytes);
        else
        {
            if (!ImageCodec.TryDecode(bytes, out var width, out var height, out var rgba))
                throw new InvalidDataException($"Texture '{label}' is not a supported image.");
            image = TextureImageData.FromOwnedRgba(width, height, rgba);
        }
        return Create(device, label, image, filter, wrap);
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
            var levels = new byte[image.MipLevels.Count][];
            for (var level = 0; level < levels.Length; level++) levels[level] = image.DecodeRgba(level);
            upload = new TextureImageData(image.Width, image.Height, TextureCompressionFormat.Rgba32, levels).PackLevels();
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
