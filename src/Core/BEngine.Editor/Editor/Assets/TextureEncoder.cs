using BEngine.Rendering;
using BCnEncoder.Encoder;

namespace BEngine.Editor;

internal static class TextureEncoder
{
    internal static byte[] Encode(int width, int height, byte[] pixels, TextureCompressionFormat format,
        bool mipMaps, bool sRgb, CancellationToken cancellationToken)
    {
        if (format == TextureCompressionFormat.Automatic) format = TextureCompressionFormat.Rgba32;
        // Preserve the established PNG artifact for the default uncompressed, non-mipmapped import.
        if (format == TextureCompressionFormat.Rgba32 && !mipMaps) return PngImageCodec.EncodeRgba(width, height, pixels);
        _ = TextureImageData.LevelSize(width, height, format);
        var levels = new List<byte[]>(); var mipWidth = width; var mipHeight = height;
        var encoder = new BcEncoder();
        encoder.Options.IsParallel = false;
        encoder.OutputOptions.GenerateMipMaps = false;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        var mobile = format is TextureCompressionFormat.Etc2Rgba8 or TextureCompressionFormat.Astc4x4;
        if (format != TextureCompressionFormat.Rgba32 && !mobile) encoder.OutputOptions.Format = TextureImageData.ToBcFormat(format);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            levels.Add(format == TextureCompressionFormat.Rgba32 ? pixels : mobile
                ? MobileTextureEncoder.Encode(mipWidth, mipHeight, pixels, format, sRgb, cancellationToken) :
                encoder.EncodeToRawBytesAsync(pixels, mipWidth, mipHeight, PixelFormat.Rgba32, cancellationToken)
                    .GetAwaiter().GetResult()[0]);
            if (!mipMaps || (mipWidth == 1 && mipHeight == 1)) break;
            var nextWidth = Math.Max(1, mipWidth / 2); var nextHeight = Math.Max(1, mipHeight / 2);
            pixels = TexturePixelProcessing.ResizeRgba(pixels, mipWidth, mipHeight, nextWidth, nextHeight, sRgb: sRgb);
            mipWidth = nextWidth; mipHeight = nextHeight;
        }
        return new TextureImageData(width, height, format, levels).Serialize();
    }
}
