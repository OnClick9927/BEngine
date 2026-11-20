using System.Buffers.Binary;
using System.Text;

namespace BEngine;

/// <summary>
/// Published atlas DTOs bypass YAML in the default runtime. The original source
/// stays in the envelope so a converter registered after packing is replayed.
/// Editable source, imported artifacts, addresses and Sprite identities stay YAML.
/// </summary>
internal static class TextureAtlasRuntimeData
{
    private const int HeaderBytes = 16;
    private const int MaximumPartBytes = BinaryDataSerializer.MaximumDocumentBytes;
    private static ReadOnlySpan<byte> Prefix => "BTAR"u8;
    private static ReadOnlySpan<byte> Magic => "BTAR\x01\0\0\0"u8;

    internal static bool IsRuntimeData(ReadOnlySpan<byte> bytes) => bytes.Length >= Magic.Length && bytes.StartsWith(Prefix) &&
        bytes[5] == 0 && bytes[6] == 0 && bytes[7] == 0;

    internal static TextureAtlas Deserialize(ReadOnlySpan<byte> bytes)
    {
        if (!IsRuntimeData(bytes)) return TextureAtlas.Deserialize(Encoding.UTF8.GetString(bytes));
        ReadEnvelope(bytes, out var binary, out var source);
        var exclusive = YamlUtility.TryGetExclusiveConverterVersion(TextureAtlasRegistration.Converter, out var version);
        // Validate the compiled half even when a foreign converter selects the
        // source half. Corrupt compiled data is never silently treated as text.
        var dto = BinaryDataSerializer.Deserialize<TextureAtlasYamlData>(binary);
        if (!exclusive || !YamlUtility.TryGetExclusiveConverterVersion(TextureAtlasRegistration.Converter, out var after) || after != version)
            return TextureAtlas.Deserialize(Encoding.UTF8.GetString(source));
        return CreateValidated(dto);
    }

    internal static TextureAtlas CreateValidated(TextureAtlasYamlData dto)
    {
        var atlas = dto.ToAsset();
        try { atlas.Validate(); return atlas; }
        catch { BObject.DestroyImmediate(atlas); throw; }
    }

    private static void ReadEnvelope(ReadOnlySpan<byte> bytes, out ReadOnlySpan<byte> binary, out ReadOnlySpan<byte> source)
    {
        if (bytes.Length < HeaderBytes || !bytes[..8].SequenceEqual(Magic))
            throw new InvalidDataException("Unsupported or truncated runtime TextureAtlas header.");
        var binaryLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        var sourceLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
        if (binaryLength is <= 0 or > MaximumPartBytes || sourceLength is <= 0 or > MaximumPartBytes ||
            HeaderBytes + (long)binaryLength + sourceLength != bytes.Length)
            throw new InvalidDataException("Runtime TextureAtlas payload lengths are invalid.");
        binary = bytes.Slice(HeaderBytes, binaryLength);
        source = bytes.Slice(HeaderBytes + binaryLength, sourceLength);
        if (!BinaryDataSerializer.IsBinaryData(binary))
            throw new InvalidDataException("Runtime TextureAtlas payload is not compiled data.");
    }

    internal static byte[] WriteEnvelope(ReadOnlySpan<byte> binary, ReadOnlySpan<byte> source)
    {
        if (binary.Length is <= 0 or > MaximumPartBytes || source.Length is <= 0 or > MaximumPartBytes)
            throw new InvalidDataException("Runtime TextureAtlas payload exceeds its size limit.");
        var result = new byte[checked(HeaderBytes + binary.Length + source.Length)];
        Magic.CopyTo(result);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), binary.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12), source.Length);
        binary.CopyTo(result.AsSpan(HeaderBytes));
        source.CopyTo(result.AsSpan(HeaderBytes + binary.Length));
        return result;
    }

}
