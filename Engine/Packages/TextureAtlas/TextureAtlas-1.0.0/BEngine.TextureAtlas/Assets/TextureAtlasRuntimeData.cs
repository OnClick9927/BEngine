using System.Buffers.Binary;
using System.Text;
using YamlDotNet.Core;

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

    internal static byte[] CompileRuntimeBytes(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var original = Deserialize(bytes.Span);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsRuntimeData(bytes.Span) ||
                !YamlUtility.TryGetExclusiveConverterVersion(TextureAtlasRegistration.Converter, out var version))
                return bytes.ToArray();
            try
            {
                // Preserve original DTO defaults/legacy fields. FromAsset would
                // capture a random generated Id when the source omitted it and
                // cannot round-trip legacy path-only Sources.
                var dto = YamlUtility.Deserialize<TextureAtlasYamlData>(Encoding.UTF8.GetString(bytes.Span));
                var canonical = YamlUtility.SerializeToUtf8Bytes(dto);
                cancellationToken.ThrowIfCancellationRequested();
                var binary = BinaryDataSerializer.CompileYaml(canonical, cancellationToken);
                var restored = BinaryDataSerializer.Deserialize<TextureAtlasYamlData>(binary);
                cancellationToken.ThrowIfCancellationRequested();
                if (SameData(dto, restored) &&
                    YamlUtility.TryGetExclusiveConverterVersion(TextureAtlasRegistration.Converter, out var after) && after == version)
                {
                    var validated = CreateValidated(restored);
                    BObject.DestroyImmediate(validated);
                    return WriteEnvelope(binary, bytes.Span);
                }
            }
            catch (Exception error) when (error is InvalidDataException or YamlException or
                                          NotSupportedException or ArgumentException or InvalidOperationException)
            {
                // A source accepted by the ordinary reader may use a custom
                // conversion or valid construct outside the compact DTO format.
            }
            cancellationToken.ThrowIfCancellationRequested();
            return bytes.ToArray();
        }
        finally { BObject.DestroyImmediate(original); }
    }

    private static TextureAtlas CreateValidated(TextureAtlasYamlData dto)
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

    private static byte[] WriteEnvelope(ReadOnlySpan<byte> binary, ReadOnlySpan<byte> source)
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

    // Build-only closed DTO comparison. No Type, converter, Assembly or mutable
    // authoring object is cached by the runtime format.
    private static bool SameData(TextureAtlasYamlData a, TextureAtlasYamlData b) =>
        a.Format == b.Format && a.Texture == b.Texture && a.Width == b.Width && a.Height == b.Height &&
        a.MaxSize == b.MaxSize && a.Padding == b.Padding && a.Extrude == b.Extrude &&
        a.Id == b.Id && a.Name == b.Name && a.HideFlags == b.HideFlags && a.Version == b.Version &&
        SameList(a.SpriteReferences, b.SpriteReferences, static (left, right) => left == right) &&
        SameList(a.Sprites, b.Sprites, static (left, right) => left is null || right is null ? ReferenceEquals(left, right) :
            left.Name == right.Name && left.Source == right.Source && left.X == right.X && left.Y == right.Y &&
            left.Width == right.Width && left.Height == right.Height && left.PivotX == right.PivotX && left.PivotY == right.PivotY) &&
        SameList(a.Sources, b.Sources, static (left, right) => left is null || right is null ? ReferenceEquals(left, right) :
            left.OwnerGuid == right.OwnerGuid && left.LocalIdentifier == right.LocalIdentifier &&
            left.Texture == right.Texture && left.Path == right.Path && left.AssetPath == right.AssetPath &&
            left.Name == right.Name && left.PivotX == right.PivotX && left.PivotY == right.PivotY);

    private static bool SameList<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b, Func<T, T, bool> same)
    {
        if (a is null || b is null) return ReferenceEquals(a, b);
        if (a.Count != b.Count) return false;
        for (var index = 0; index < a.Count; index++) if (!same(a[index], b[index])) return false;
        return true;
    }
}
