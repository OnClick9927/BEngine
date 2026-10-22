using StbImageSharp;

namespace BEngine.Rendering;

/// <summary>Bounded, top-left-origin RGBA8 decoding shared by importers and runtime assets.</summary>
public static class ImageCodec
{
    public const int MaximumDecodedBytes = 256 * 1024 * 1024;
    public static bool IsSupportedPath(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".gif";

    public static bool TryGetInfo(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = height = 0;
        if (bytes.IsEmpty || (bytes.Length > MaximumDecodedBytes && !TextureImageData.IsContainer(bytes))) return false;
        try
        {
            if (TextureImageData.IsContainer(bytes))
            {
                var texture = TextureImageData.Inspect(bytes);
                width = texture.Width; height = texture.Height; return true;
            }
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            var info = ImageInfo.FromStream(stream);
            if (info is null || !ValidSize(info.Value.Width, info.Value.Height)) return false;
            width = info.Value.Width;
            height = info.Value.Height;
            if (JpegOrientation.Read(bytes) >= 5) (width, height) = (height, width);
            return true;
        }
        catch (Exception error) when (IsDecodeError(error)) { return false; }
    }

    public static bool TryDecode(ReadOnlySpan<byte> bytes, out int width, out int height, out byte[] rgba)
    {
        width = height = 0;
        rgba = [];
        try
        {
            if (TextureImageData.IsContainer(bytes))
            {
                var texture = TextureImageData.Deserialize(bytes);
                rgba = texture.DecodeRgba(); width = texture.Width; height = texture.Height; return true;
            }
            if (!TryGetInfo(bytes, out var expectedWidth, out var expectedHeight)) return false;
            var image = ImageResult.FromMemory(bytes.ToArray(), ColorComponents.RedGreenBlueAlpha);
            if (!ValidSize(image.Width, image.Height) || image.Data.Length != (long)image.Width * image.Height * 4)
                return false;
            var pixels = JpegOrientation.Transform(image.Data, image.Width, image.Height,
                JpegOrientation.Read(bytes));
            if (pixels.Length != (long)expectedWidth * expectedHeight * 4) return false;
            width = expectedWidth;
            height = expectedHeight;
            rgba = pixels;
            return true;
        }
        catch (Exception error) when (IsDecodeError(error)) { return false; }
    }

    internal static bool ValidSize(int width, int height) => width is > 0 and <= 16384 &&
        height is > 0 and <= 16384 && (long)width * height * 4 <= MaximumDecodedBytes;

    // stb reports malformed/unsupported images as plain Exception, in addition to stream errors.
    private static bool IsDecodeError(Exception error) => error.GetType() == typeof(Exception) || error is
        InvalidDataException or EndOfStreamException or ArgumentException or OverflowException or IndexOutOfRangeException or NotSupportedException;
}
