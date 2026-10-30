using System.Buffers.Binary;
using System.IO.Compression;
using BEngine.Platform;
using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public sealed class FileUIRenderResourceResolver : IUIRenderResourceResolver, IUIImportedTextureResolver
{
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _paths;
    private readonly PlatformUtility _platform;

    public static FileUIRenderResourceResolver Shared { get; } = new(
        RuntimeServiceAccess.FileSystem,
        RuntimeServiceAccess.PathService,
        RuntimeServiceAccess.PlatformService);

    public FileUIRenderResourceResolver()
        : this(RuntimeServiceAccess.FileSystem, RuntimeServiceAccess.PathService,
            RuntimeServiceAccess.PlatformService) { }

    public FileUIRenderResourceResolver(
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _paths = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _platform = platformService ?? throw new ArgumentNullException(nameof(platformService));
    }

    public bool TryResolveTexture(string source, out RasterizedImage texture)
    {
        texture = default;
        if (string.IsNullOrWhiteSpace(source)) return false;
        try
        {
            var path = ResolvePath(source);
            var exists = _fileSystem.FileExists(path);
            var bytes = exists
                ? _fileSystem.ReadAllBytes(path)
                : Resources.Load<byte[]>(source);
            if (bytes is null) return false;
            return TryDecodeTexture(bytes, exists ? path : source, out texture);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or InvalidOperationException or
                                          ArgumentException or NotSupportedException or OverflowException)
        {
            return false;
        }
    }

    public bool TryResolveImportedTexture(string source, out TextureImageData texture)
    {
        texture = null!;
        if (string.IsNullOrWhiteSpace(source)) return false;
        try
        {
            var path = ResolvePath(source);
            var bytes = _fileSystem.FileExists(path) ? _fileSystem.ReadAllBytes(path) : Resources.Load<byte[]>(source);
            if (bytes is null || !TextureImageData.IsContainer(bytes)) return false;
            texture = TextureImageData.Deserialize(bytes);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                     ArgumentException or NotSupportedException or InvalidOperationException or OverflowException)
        {
            return false;
        }
    }

    // The renderer normally probes the imported path before the rasterized
    // path. A PNG is not an imported container, so those two probes previously
    // read and cloned the same resource twice. Keep both public contracts and
    // custom resolvers unchanged; the sealed default resolver can share bytes.
    internal bool TryResolveTextureData(string source, out TextureImageData? imported,
        out RasterizedImage texture)
    {
        imported = null;
        texture = default;
        if (string.IsNullOrWhiteSpace(source)) return false;
        byte[] bytes;
        string effectivePath;
        try
        {
            var path = ResolvePath(source);
            var exists = _fileSystem.FileExists(path);
            var read = exists ? _fileSystem.ReadAllBytes(path) : Resources.Load<byte[]>(source);
            // Preserve the old second probe after an unsuccessful first read.
            if (read is null) return TryResolveTexture(source, out texture);
            bytes = read;
            if (TextureImageData.IsContainer(bytes))
            {
                imported = TextureImageData.Deserialize(bytes);
                return true;
            }
            effectivePath = exists ? path : source;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                     ArgumentException or NotSupportedException or InvalidOperationException or OverflowException)
        {
            return TryResolveTexture(source, out texture);
        }
        try { return TryDecodeTexture(bytes, effectivePath, out texture); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                     InvalidDataException or InvalidOperationException or
                                     ArgumentException or NotSupportedException or OverflowException)
        {
            return false;
        }
    }

    private bool TryDecodeTexture(byte[] bytes, string source, out RasterizedImage texture)
    {
        texture = default;
        if (TextureImageData.IsContainer(bytes))
        {
            var imported = TextureImageData.Deserialize(bytes);
            texture = new RasterizedImage(imported.Width, imported.Height,
                GraphicsTextureFormat.Rgba8Unorm, imported.DecodeRgba());
            return true;
        }
        var extension = _paths.GetExtension(source).ToLowerInvariant();
        return extension switch
        {
            ".png" => PortablePngDecoder.TryDecode(bytes, out texture),
            ".rgba" => TryDecodeRawRgba(bytes, out texture),
            _ => false
        };
    }

    private string ResolvePath(string source)
    {
        if (_paths.IsPathRooted(source)) return _paths.GetFullPath(source);
        var normalized = source.Replace('/', _paths.DirectorySeparatorChar);
        var currentPath = _paths.GetFullPath(normalized);
        if (_fileSystem.FileExists(currentPath)) return currentPath;
        var dataPath = _paths.GetFullPath(Application.dataPath);
        if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith($"Assets{_paths.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            var projectRoot = _paths.GetFileName(dataPath).Equals("Assets", StringComparison.OrdinalIgnoreCase)
                ? _paths.GetDirectoryName(dataPath) ?? _platform.CurrentDirectory
                : _platform.CurrentDirectory;
            return _paths.GetFullPath(_paths.Combine(projectRoot, normalized));
        }
        return _paths.GetFullPath(_paths.Combine(dataPath, normalized));
    }

    private static bool TryDecodeRawRgba(byte[] bytes, out RasterizedImage texture)
    {
        texture = default;
        if (bytes.Length == 0 || bytes.Length % 4 != 0) return false;
        var side = (int)Math.Sqrt(bytes.Length / 4d);
        if (checked(side * side * 4) != bytes.Length) return false;
        texture = new RasterizedImage(side, side, GraphicsTextureFormat.Rgba8Unorm, bytes);
        return true;
    }

    private static class PortablePngDecoder
    {
        private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

        public static bool TryDecode(ReadOnlySpan<byte> data, out RasterizedImage texture)
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

        private static bool Decode(ReadOnlySpan<byte> data, out RasterizedImage texture)
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
            // These loops are bounded by the PNG data, not by the editor callback limit.
            using var chunkBudget = LoopExecutionGuard.AllowIterations((data.Length - Signature.Length) / 12);
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

            // Compact scanlines towards the start of the same array. Each
            // output byte precedes its encoded input, so forward reconstruction
            // preserves unread bytes as well as the previous decoded row.
            Unfilter(filtered, filtered, rowLength, sourceBytesPerPixel, height);
            // RGBA rows already have the required channel order and straight
            // alpha. Reuse them instead of allocating and copying every pixel.
            ReadOnlyMemory<byte> rgba = filtered.AsMemory(0, checked(rowLength * height));
            if (colorType != 6)
            {
                var converted = new byte[checked(width * height * 4)];
                ConvertToRgba(filtered, converted, colorType, palette, transparency);
                rgba = converted;
            }
            texture = new RasterizedImage(width, height, GraphicsTextureFormat.Rgba8Unorm, rgba);
            return true;
        }

        private static void Unfilter(byte[] filtered, byte[] raw, int rowLength, int bytesPerPixel, int height)
        {
            using var rowBudget = LoopExecutionGuard.AllowIterations(Math.Max(height, rowLength));
            var source = 0;
            for (var y = 0; y < height; y++)
            {
                var filter = filtered[source++];
                var row = y * rowLength;
                var input = filtered.AsSpan(source, rowLength);
                var output = raw.AsSpan(row, rowLength);
                var previous = y > 0 ? raw.AsSpan(row - rowLength, rowLength) : default;
                source += rowLength;
                // Select once per scanline. Filter 0 is common in generated UI
                // assets and can copy a complete row without per-byte branches.
                switch (filter)
                {
                    case 0:
                        input.CopyTo(output);
                        break;
                    case 1:
                        UnfilterSub(input, output, bytesPerPixel);
                        break;
                    case 2:
                        if (y == 0) input.CopyTo(output);
                        else
                            for (var x = 0; x < rowLength; x++)
                                output[x] = unchecked((byte)(input[x] + previous[x]));
                        break;
                    case 3:
                        if (y == 0)
                        {
                            input[..bytesPerPixel].CopyTo(output);
                            for (var x = bytesPerPixel; x < rowLength; x++)
                                output[x] = unchecked((byte)(input[x] + (output[x - bytesPerPixel] >> 1)));
                        }
                        else
                        {
                            for (var x = 0; x < bytesPerPixel; x++)
                                output[x] = unchecked((byte)(input[x] + (previous[x] >> 1)));
                            for (var x = bytesPerPixel; x < rowLength; x++)
                                output[x] = unchecked((byte)(input[x] + ((output[x - bytesPerPixel] + previous[x]) >> 1)));
                        }
                        break;
                    case 4:
                        if (y == 0) UnfilterSub(input, output, bytesPerPixel);
                        else
                        {
                            for (var x = 0; x < bytesPerPixel; x++)
                                output[x] = unchecked((byte)(input[x] + previous[x]));
                            for (var x = bytesPerPixel; x < rowLength; x++)
                                output[x] = unchecked((byte)(input[x] +
                                    Paeth(output[x - bytesPerPixel], previous[x], previous[x - bytesPerPixel])));
                        }
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported PNG row filter {filter}.");
                }
            }
        }

        private static void UnfilterSub(ReadOnlySpan<byte> input, Span<byte> output, int bytesPerPixel)
        {
            input[..bytesPerPixel].CopyTo(output);
            for (var x = bytesPerPixel; x < input.Length; x++)
                output[x] = unchecked((byte)(input[x] + output[x - bytesPerPixel]));
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
            using var pixelBudget = LoopExecutionGuard.AllowIterations(output.Length / 4);
            if (colorType == 2)
            {
                // RGB is the common opaque backdrop path. Select its format
                // once rather than dispatching the same case for every pixel.
                for (int sourceIndex = 0, outputIndex = 0; outputIndex < output.Length; sourceIndex += 3, outputIndex += 4)
                {
                    output[outputIndex] = source[sourceIndex];
                    output[outputIndex + 1] = source[sourceIndex + 1];
                    output[outputIndex + 2] = source[sourceIndex + 2];
                    output[outputIndex + 3] = 255;
                }
                return;
            }
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
