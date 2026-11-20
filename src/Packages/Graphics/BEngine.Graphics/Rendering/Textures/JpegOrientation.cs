using System.Buffers.Binary;

namespace BEngine.Rendering;

internal static class JpegOrientation
{
    internal static int Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 255 || bytes[1] != 216) return 1;
        var offset = 2;
        while (offset < bytes.Length)
        {
            if (bytes[offset++] != 255) return 1;
            while (offset < bytes.Length && bytes[offset] == 255) offset++;
            if (offset >= bytes.Length) return 1;
            var marker = bytes[offset++];
            if (marker is 217 or 218) return 1;
            if (marker is 1 or >= 208 and <= 215) continue;
            if (offset > bytes.Length - 2) return 1;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
            if (length < 2 || length > bytes.Length - offset) return 1;
            var segment = bytes.Slice(offset + 2, length - 2);
            if (marker == 225 && segment.Length >= 14 && segment[..6].SequenceEqual("Exif\0\0"u8))
                return ReadTiff(segment[6..]);
            offset += length;
        }
        return 1;
    }

    private static int ReadTiff(ReadOnlySpan<byte> data)
    {
        var little = data[..2].SequenceEqual("II"u8);
        if (!little && !data[..2].SequenceEqual("MM"u8)) return 1;
        if (U16(data[2..], little) != 42) return 1;
        var offset = U32(data[4..], little);
        if (offset > data.Length - 2) return 1;
        var count = U16(data[(int)offset..], little);
        offset += 2;
        for (var i = 0; i < count && offset <= data.Length - 12; i++, offset += 12)
        {
            var entry = data[(int)offset..];
            if (U16(entry, little) != 0x112 || U16(entry[2..], little) != 3 || U32(entry[4..], little) != 1)
                continue;
            var value = U16(entry[8..], little);
            return value is >= 1 and <= 8 ? value : 1;
        }
        return 1;
    }

    private static ushort U16(ReadOnlySpan<byte> data, bool little) => little
        ? BinaryPrimitives.ReadUInt16LittleEndian(data) : BinaryPrimitives.ReadUInt16BigEndian(data);
    private static uint U32(ReadOnlySpan<byte> data, bool little) => little
        ? BinaryPrimitives.ReadUInt32LittleEndian(data) : BinaryPrimitives.ReadUInt32BigEndian(data);

    internal static byte[] Transform(byte[] pixels, int width, int height, int orientation)
    {
        if (orientation == 1) return pixels;
        var outputWidth = orientation >= 5 ? height : width;
        var result = new byte[pixels.Length];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var (dx, dy) = orientation switch
            {
                2 => (width - 1 - x, y),
                3 => (width - 1 - x, height - 1 - y),
                4 => (x, height - 1 - y),
                5 => (y, x),
                6 => (height - 1 - y, x),
                7 => (height - 1 - y, width - 1 - x),
                8 => (y, width - 1 - x),
                _ => (x, y)
            };
            pixels.AsSpan((y * width + x) * 4, 4).CopyTo(result.AsSpan((dy * outputWidth + dx) * 4, 4));
        }
        return result;
    }
}
