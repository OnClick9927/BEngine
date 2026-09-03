using System.Buffers.Binary;
using System.IO.Compression;
using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public sealed class FileUIRenderResourceResolver : IUIRenderResourceResolver
{
    public static FileUIRenderResourceResolver Shared { get; } = new();

    public bool TryResolveTexture(string source, out UIRenderTextureData texture)
    {
        texture = default;
        if (string.IsNullOrWhiteSpace(source)) return false;
        try
        {
            var path = ResolvePath(source);
            var bytes = File.Exists(path)
                ? File.ReadAllBytes(path)
                : Resources.Load<byte[]>(source);
            if (bytes is null) return false;
            var extension = Path.GetExtension(File.Exists(path) ? path : source).ToLowerInvariant();
            return extension switch
            {
                ".png" => PortablePngDecoder.TryDecode(bytes, out texture),
                ".rgba" => TryDecodeRawRgba(bytes, out texture),
                _ => false
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or InvalidOperationException or
                                          ArgumentException or NotSupportedException or OverflowException)
        {
            return false;
        }
    }

    private static string ResolvePath(string source)
    {
        if (Path.IsPathRooted(source)) return Path.GetFullPath(source);
        var normalized = source.Replace('/', Path.DirectorySeparatorChar);
        var currentPath = Path.GetFullPath(normalized);
        if (File.Exists(currentPath)) return currentPath;
        var dataPath = Path.GetFullPath(Application.dataPath);
        if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith($"Assets{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            var projectRoot = Path.GetFileName(dataPath).Equals("Assets", StringComparison.OrdinalIgnoreCase)
                ? Directory.GetParent(dataPath)?.FullName ?? Directory.GetCurrentDirectory()
                : Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(projectRoot, normalized));
        }
        return Path.GetFullPath(Path.Combine(dataPath, normalized));
    }

    private static bool TryDecodeRawRgba(byte[] bytes, out UIRenderTextureData texture)
    {
        texture = default;
        if (bytes.Length == 0 || bytes.Length % 4 != 0) return false;
        var side = (int)Math.Sqrt(bytes.Length / 4d);
        if (checked(side * side * 4) != bytes.Length) return false;
        texture = new UIRenderTextureData(side, side, GraphicsTextureFormat.Rgba8Unorm, bytes);
        return true;
    }

    private static class PortablePngDecoder
    {
        private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

        public static bool TryDecode(ReadOnlySpan<byte> data, out UIRenderTextureData texture)
        {
            texture = default;
            try
            {
                return Decode(data, out texture);
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException or
                                              OverflowException or EndOfStreamException)
            {
                return false;
            }
        }

        private static bool Decode(ReadOnlySpan<byte> data, out UIRenderTextureData texture)
        {
            texture = default;
            if (data.Length < Signature.Length || !data[..Signature.Length].SequenceEqual(Signature)) return false;
            var offset = Signature.Length;
            var width = 0;
            var height = 0;
            byte colorType = byte.MaxValue;
            byte bitDepth = 0;
            byte interlace = 0;
            byte[] palette = [];
            byte[] transparency = [];
            using var compressed = new MemoryStream();
            while (offset + 12 <= data.Length)
            {
                var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4)));
                offset += 4;
                if (length < 0 || offset + 4 + length + 4 > data.Length) return false;
                var type = data.Slice(offset, 4);
                offset += 4;
                var chunk = data.Slice(offset, length);
                offset += length + 4;
                if (type.SequenceEqual("IHDR"u8))
                {
                    if (chunk.Length != 13) return false;
                    width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunk[..4]));
                    height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(4, 4)));
                    bitDepth = chunk[8];
                    colorType = chunk[9];
                    if (chunk[10] != 0 || chunk[11] != 0) return false;
                    interlace = chunk[12];
                }
                else if (type.SequenceEqual("PLTE"u8)) palette = chunk.ToArray();
                else if (type.SequenceEqual("tRNS"u8)) transparency = chunk.ToArray();
                else if (type.SequenceEqual("IDAT"u8)) compressed.Write(chunk);
                else if (type.SequenceEqual("IEND"u8)) break;
            }

            if (width <= 0 || height <= 0 || width > 32768 || height > 32768 || bitDepth != 8 || interlace != 0)
                return false;
            var sourceBytesPerPixel = colorType switch
            {
                0 => 1,
                2 => 3,
                3 => 1,
                4 => 2,
                6 => 4,
                _ => 0
            };
            if (sourceBytesPerPixel == 0 || compressed.Length == 0) return false;
            var rowLength = checked(width * sourceBytesPerPixel);
            var filtered = new byte[checked((rowLength + 1) * height)];
            compressed.Position = 0;
            using (var inflater = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true))
            {
                inflater.ReadExactly(filtered);
            }

            var raw = new byte[checked(rowLength * height)];
            Unfilter(filtered, raw, rowLength, sourceBytesPerPixel, height);
            var rgba = new byte[checked(width * height * 4)];
            ConvertToRgba(raw, rgba, colorType, palette, transparency);
            texture = new UIRenderTextureData(width, height, GraphicsTextureFormat.Rgba8Unorm, rgba);
            return true;
        }

        private static void Unfilter(byte[] filtered, byte[] raw, int rowLength, int bytesPerPixel, int height)
        {
            var source = 0;
            for (var y = 0; y < height; y++)
            {
                var filter = filtered[source++];
                var row = y * rowLength;
                var previous = row - rowLength;
                for (var x = 0; x < rowLength; x++)
                {
                    var value = filtered[source++];
                    var left = x >= bytesPerPixel ? raw[row + x - bytesPerPixel] : 0;
                    var up = y > 0 ? raw[previous + x] : 0;
                    var upperLeft = y > 0 && x >= bytesPerPixel ? raw[previous + x - bytesPerPixel] : 0;
                    raw[row + x] = filter switch
                    {
                        0 => value,
                        1 => unchecked((byte)(value + left)),
                        2 => unchecked((byte)(value + up)),
                        3 => unchecked((byte)(value + ((left + up) >> 1))),
                        4 => unchecked((byte)(value + Paeth(left, up, upperLeft))),
                        _ => throw new InvalidDataException($"Unsupported PNG row filter {filter}.")
                    };
                }
            }
        }

        private static int Paeth(int left, int up, int upperLeft)
        {
            var prediction = left + up - upperLeft;
            var leftDistance = Math.Abs(prediction - left);
            var upDistance = Math.Abs(prediction - up);
            var upperLeftDistance = Math.Abs(prediction - upperLeft);
            return leftDistance <= upDistance && leftDistance <= upperLeftDistance
                ? left
                : upDistance <= upperLeftDistance ? up : upperLeft;
        }

        private static void ConvertToRgba(
            byte[] source,
            byte[] output,
            byte colorType,
            byte[] palette,
            byte[] transparency)
        {
            var sourceOffset = 0;
            for (var outputOffset = 0; outputOffset < output.Length; outputOffset += 4)
            {
                switch (colorType)
                {
                    case 0:
                        var gray = source[sourceOffset++];
                        output[outputOffset] = gray;
                        output[outputOffset + 1] = gray;
                        output[outputOffset + 2] = gray;
                        output[outputOffset + 3] = 255;
                        break;
                    case 2:
                        output[outputOffset] = source[sourceOffset++];
                        output[outputOffset + 1] = source[sourceOffset++];
                        output[outputOffset + 2] = source[sourceOffset++];
                        output[outputOffset + 3] = 255;
                        break;
                    case 3:
                        var paletteIndex = source[sourceOffset++];
                        var paletteOffset = paletteIndex * 3;
                        if (paletteOffset + 2 >= palette.Length)
                            throw new InvalidDataException("PNG palette index is outside PLTE.");
                        output[outputOffset] = palette[paletteOffset];
                        output[outputOffset + 1] = palette[paletteOffset + 1];
                        output[outputOffset + 2] = palette[paletteOffset + 2];
                        output[outputOffset + 3] = paletteIndex < transparency.Length
                            ? transparency[paletteIndex]
                            : (byte)255;
                        break;
                    case 4:
                        var grayscale = source[sourceOffset++];
                        output[outputOffset] = grayscale;
                        output[outputOffset + 1] = grayscale;
                        output[outputOffset + 2] = grayscale;
                        output[outputOffset + 3] = source[sourceOffset++];
                        break;
                    case 6:
                        source.AsSpan(sourceOffset, 4).CopyTo(output.AsSpan(outputOffset, 4));
                        sourceOffset += 4;
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported PNG color type {colorType}.");
                }
            }
        }
    }
}
