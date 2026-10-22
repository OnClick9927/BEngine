using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;

namespace BEngine.Rendering;

/// <summary>Validated imported texture, with tightly packed mip levels in GPU block order.</summary>
public sealed class TextureImageData
{
    internal static ReadOnlySpan<byte> Signature => "BTEX0001"u8;
    public int Width { get; }
    public int Height { get; }
    public TextureCompressionFormat Format { get; }
    public IReadOnlyList<byte[]> MipLevels { get; }
    public int DataSize => MipLevels.Sum(level => level.Length);

    public TextureImageData(int width, int height, TextureCompressionFormat format, IEnumerable<byte[]> mipLevels)
        : this(width, height, format, mipLevels.Select(level => level.ToArray()).ToArray()) { }

    // The decoder creates these arrays exclusively for the upload. Transferring
    // their ownership avoids another full image copy while the public
    // constructor retains its defensive-copy behavior for callers' buffers.
    internal static TextureImageData FromOwnedRgba(int width, int height, byte[] rgba) =>
        new(width, height, TextureCompressionFormat.Rgba32, [rgba]);

    private TextureImageData(int width, int height, TextureCompressionFormat format, byte[][] levels)
    {
        if (!ImageCodec.ValidSize(width, height)) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width; Height = height; Format = format;
        var maximum = 1 + (int)Math.Log2(Math.Max(width, height));
        if (levels.Length < 1 || levels.Length > maximum) throw new InvalidDataException("Invalid texture mip count.");
        for (var i = 0; i < levels.Length; i++)
            if (levels[i].Length != LevelSize(Math.Max(1, width >> i), Math.Max(1, height >> i), format))
                throw new InvalidDataException($"Texture mip {i} has an invalid byte length.");
        MipLevels = Array.AsReadOnly(levels);
    }

    public byte[] DecodeRgba(int level = 0)
    {
        if ((uint)level >= MipLevels.Count) throw new ArgumentOutOfRangeException(nameof(level));
        if (Format == TextureCompressionFormat.Rgba32) return MipLevels[level].ToArray();
        if (Format is TextureCompressionFormat.Etc2Rgba8 or TextureCompressionFormat.Astc4x4)
        {
            var width = Math.Max(1, Width >> level); var height = Math.Max(1, Height >> level);
            var pixels = new byte[checked(width * height * 4)];
            var consumed = Format == TextureCompressionFormat.Etc2Rgba8
                ? AssetRipper.TextureDecoder.Etc.EtcDecoder.DecompressETC2A8<AssetRipper.TextureDecoder.Rgb.Formats.ColorRGBA<byte>, byte>(
                    MipLevels[level], width, height, pixels)
                : AssetRipper.TextureDecoder.Astc.AstcDecoder.DecodeASTC<AssetRipper.TextureDecoder.Rgb.Formats.ColorRGBA<byte>, byte>(
                    MipLevels[level], width, height, 4, 4, pixels);
            if (consumed != MipLevels[level].Length) throw new InvalidDataException("Texture decoder did not consume all blocks.");
            return pixels;
        }
        var decoder = new BcDecoder(); decoder.Options.IsParallel = false;
        var colors = decoder.DecodeRaw(MipLevels[level], Math.Max(1, Width >> level),
            Math.Max(1, Height >> level), ToBcFormat(Format));
        return MemoryMarshal.AsBytes(colors.AsSpan()).ToArray();
    }

    public byte[] PackLevels()
    {
        var result = new byte[DataSize]; var offset = 0;
        foreach (var level in MipLevels) { level.CopyTo(result, offset); offset += level.Length; }
        return result;
    }

    public byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Signature); writer.Write(Width); writer.Write(Height);
        writer.Write((int)Format); writer.Write(MipLevels.Count);
        foreach (var level in MipLevels) { writer.Write(level.Length); writer.Write(level); }
        writer.Flush();
        var hash = SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
        writer.Write(hash); writer.Flush(); return stream.ToArray();
    }

    public static bool IsContainer(ReadOnlySpan<byte> bytes) => bytes.StartsWith(Signature);

    public static TextureImageData Deserialize(ReadOnlySpan<byte> bytes)
    {
        var (width, height, format, count) = Inspect(bytes);
        var levels = new byte[count][]; var offset = 24;
        for (var i = 0; i < count; i++)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]); offset += 4;
            levels[i] = bytes.Slice(offset, length).ToArray(); offset += length;
        }
        return new TextureImageData(width, height, format, levels);
    }

    internal static (int Width, int Height, TextureCompressionFormat Format, int MipCount) Inspect(ReadOnlySpan<byte> bytes)
    {
        if (!IsContainer(bytes) || bytes.Length < 60 || bytes.Length > ImageCodec.MaximumDecodedBytes * 2L)
            throw new InvalidDataException("Invalid texture artifact header.");
        if (!SHA256.HashData(bytes[..^32]).AsSpan().SequenceEqual(bytes[^32..]))
            throw new InvalidDataException("Texture artifact checksum mismatch.");
        var width = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
        var format = (TextureCompressionFormat)BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]);
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes[20..]);
        if (!ImageCodec.ValidSize(width, height) || count < 1 || count > 1 + (int)Math.Log2(Math.Max(width, height)))
            throw new InvalidDataException("Invalid texture artifact dimensions or mip count.");
        var offset = 24;
        for (var i = 0; i < count; i++)
        {
            if (offset > bytes.Length - 36) throw new InvalidDataException("Truncated texture mip table.");
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]); offset += 4;
            if (length != LevelSize(Math.Max(1, width >> i), Math.Max(1, height >> i), format) ||
                length > bytes.Length - 32 - offset) throw new InvalidDataException("Invalid texture mip length.");
            offset += length;
        }
        if (offset != bytes.Length - 32) throw new InvalidDataException("Unexpected trailing texture data.");
        return (width, height, format, count);
    }

    public static int LevelSize(int width, int height, TextureCompressionFormat format) => format switch
    {
        TextureCompressionFormat.Rgba32 => checked(width * height * 4),
        TextureCompressionFormat.Bc1 => checked(((width + 3) / 4) * ((height + 3) / 4) * 8),
        TextureCompressionFormat.Bc3 or TextureCompressionFormat.Bc7 or TextureCompressionFormat.Etc2Rgba8 or TextureCompressionFormat.Astc4x4 =>
            checked(((width + 3) / 4) * ((height + 3) / 4) * 16),
        _ => throw new NotSupportedException($"Texture compression {format} has no installed encoder/decoder.")
    };

    internal static CompressionFormat ToBcFormat(TextureCompressionFormat format) => format switch
    {
        TextureCompressionFormat.Bc1 => CompressionFormat.Bc1WithAlpha,
        TextureCompressionFormat.Bc3 => CompressionFormat.Bc3,
        TextureCompressionFormat.Bc7 => CompressionFormat.Bc7,
        _ => throw new NotSupportedException($"Texture compression {format} is not a BC format.")
    };
}
