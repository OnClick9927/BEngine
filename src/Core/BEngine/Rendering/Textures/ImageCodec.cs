using StbImageSharp;
using System.Buffers.Binary;

namespace BEngine.Rendering;

/// <summary>Bounded, top-left-origin RGBA8 decoding shared by importers and runtime assets.</summary>
public static class ImageCodec
{
    public const int MaximumDecodedBytes = 256 * 1024 * 1024;
    public static bool IsSupportedPath(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".gif";

    public static bool TryGetInfo(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        if (TryGetPngInfo(bytes, out width, out height)) return true;
        return TryGetInfo(bytes.ToArray(), out width, out height);
    }

    /// <summary>Inspects owned encoded bytes without making a second copy of the image.</summary>
    public static bool TryGetInfo(byte[] bytes, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        width = height = 0;
        if (bytes.Length == 0 || (bytes.Length > MaximumDecodedBytes && !TextureImageData.IsContainer(bytes))) return false;
        try
        {
            if (TextureImageData.IsContainer(bytes))
            {
                var texture = TextureImageData.Inspect(bytes);
                width = texture.Width; height = texture.Height; return true;
            }
            if (TryGetPngInfo(bytes, out width, out height)) return true;
            using var stream = new MemoryStream(bytes, writable: false);
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
        => TryDecodeCore(bytes, null, out width, out height, out rgba);

    /// <summary>Decodes owned encoded bytes without cloning the encoded payload.</summary>
    public static bool TryDecode(byte[] bytes, out int width, out int height, out byte[] rgba)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return TryDecodeCore(bytes, bytes, out width, out height, out rgba);
    }

    private static bool TryDecodeCore(ReadOnlySpan<byte> bytes, byte[]? owned, out int width, out int height, out byte[] rgba)
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
            int expectedWidth, expectedHeight;
            if (!(owned is null ? TryGetInfo(bytes, out expectedWidth, out expectedHeight)
                : TryGetInfo(owned, out expectedWidth, out expectedHeight))) return false;
            var image = ImageResult.FromMemory(owned ?? bytes.ToArray(), ColorComponents.RedGreenBlueAlpha);
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

    private static bool TryGetPngInfo(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = height = 0;
        if (bytes.Length is < 33 or > MaximumDecodedBytes || !bytes[..8].SequenceEqual<byte>([137, 80, 78, 71, 13, 10, 26, 10]) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) != 13 || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8) ||
            bytes[26] != 0 || bytes[27] != 0 || bytes[28] > 1) return false;
        var depth = bytes[24]; var color = bytes[25];
        if (!(color switch { 0 => depth is 1 or 2 or 4 or 8 or 16, 2 or 4 or 6 => depth is 8 or 16,
                3 => depth is 1 or 2 or 4 or 8, _ => false })) return false;
        var w = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]);
        var h = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]);
        if (w > int.MaxValue || h > int.MaxValue || !ValidSize((int)w, (int)h)) return false;
        width = (int)w; height = (int)h; return true;
    }

    // stb reports malformed/unsupported images as plain Exception, in addition to stream errors.
    private static bool IsDecodeError(Exception error) => error.GetType() == typeof(Exception) || error is
        InvalidDataException or EndOfStreamException or ArgumentException or OverflowException or IndexOutOfRangeException or NotSupportedException;
}
