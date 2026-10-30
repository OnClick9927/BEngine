using System.Buffers.Binary;
using System.Text.Json;
using BEngine.UIElements;

namespace BEngine.Editor;

// A build-time capability check, not a replacement for the font rasterizer's
// complete font validation. No native font library or browser API is invoked.
internal static class WebFontCapabilityValidator
{
    private const int MaximumCollectionFaces = 1024;
    private const uint CollectionTag = 0x74746366; // ttcf
    private const uint ColrTag = 0x434F4C52;
    private const uint SvgTag = 0x53564720;

    internal readonly record struct Result(int EmbeddedFonts, int Faces);

    internal static Result ValidateAsset(ReadOnlySpan<byte> bytes, string assetPath)
    {
        if (bytes.Length > SdfFontBinaryCodec.MaximumDocumentBytes)
            throw new InvalidDataException($"Font asset '{assetPath}' exceeds the 192 MB asset limit.");
        SdfFontData data;
        try
        {
            data = SdfFontBinaryCodec.IsBinary(bytes) ? SdfFontBinaryCodec.Read(bytes) :
                JsonSerializer.Deserialize<SdfFontData>(bytes) ?? throw new InvalidDataException("SDF font is empty.");
            data.Validate();
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or FormatException)
        {
            throw new InvalidDataException($"Cannot validate Web FreeType font asset '{assetPath}': {error.Message}", error);
        }
        var faces = 0;
        foreach (var source in data.SourceFonts)
            faces += ValidateOpenType(source.Data, assetPath, source.Name);
        return new(data.SourceFonts.Length, faces);
    }

    internal static int ValidateOpenType(ReadOnlySpan<byte> bytes, string assetPath, string sourceName)
    {
        if (bytes.Length is < 12 or > 64 * 1024 * 1024)
            throw Invalid(assetPath, sourceName, "the embedded source size is outside the supported bounds");
        if (UInt32(bytes, 0) != CollectionTag)
        {
            ValidateFace(bytes, 0, assetPath, sourceName, 0);
            return 1;
        }

        var version = UInt32(bytes, 4);
        if (version is not (0x00010000 or 0x00020000))
            throw Invalid(assetPath, sourceName, "the TTC version is unsupported");
        var count = UInt32(bytes, 8);
        if (count is 0 or > MaximumCollectionFaces)
            throw Invalid(assetPath, sourceName, $"the TTC face count must be between 1 and {MaximumCollectionFaces}");
        var headerLength = 12UL + count * 4UL + (version == 0x00020000 ? 12UL : 0UL);
        if (headerLength > (ulong)bytes.Length)
            throw Invalid(assetPath, sourceName, "the TTC face directory is truncated");
        if (version == 0x00020000)
        {
            var signature = (int)(12 + count * 4);
            var tag = UInt32(bytes, signature);
            var length = UInt32(bytes, signature + 4);
            var offset = UInt32(bytes, signature + 8);
            if (tag == 0 ? length != 0 || offset != 0 :
                tag != 0x44534947 || (ulong)offset + length > (ulong)bytes.Length)
                throw Invalid(assetPath, sourceName, "the TTC digital-signature range is invalid");
        }
        // Check every face, including fallback faces in a collection. The current
        // runtime selects face zero; rejecting unsupported alternate faces is a
        // deliberate conservative policy rather than silently degrading them.
        var visited = new HashSet<uint>();
        for (var face = 0; face < count; face++)
        {
            var offset = UInt32(bytes, 12 + face * 4);
            if (offset < headerLength || (ulong)offset + 12 > (ulong)bytes.Length)
                throw Invalid(assetPath, sourceName, $"TTC face {face} has an invalid directory offset");
            if (visited.Add(offset)) ValidateFace(bytes, (int)offset, assetPath, sourceName, face);
        }
        return (int)count;
    }

    private static void ValidateFace(ReadOnlySpan<byte> bytes, int offset, string assetPath, string sourceName, int face)
    {
        if ((uint)offset > (uint)bytes.Length || bytes.Length - offset < 12)
            throw Invalid(assetPath, sourceName, $"face {face} has a truncated SFNT header");
        var signature = UInt32(bytes, offset);
        if (signature is not (0x00010000 or 0x4F54544F or 0x74727565 or 0x74797031))
            throw Invalid(assetPath, sourceName, $"face {face} is not a supported SFNT/OpenType container; use Skia or convert the font");
        var count = UInt16(bytes, offset + 4);
        if (count == 0 || (ulong)offset + 12 + count * 16UL > (ulong)bytes.Length)
            throw Invalid(assetPath, sourceName, $"face {face} has an empty or truncated SFNT table directory");
        var tags = new HashSet<uint>();
        uint colrOffset = 0, colrLength = 0;
        var svg = false;
        for (var table = 0; table < count; table++)
        {
            var record = offset + 12 + table * 16;
            var tag = UInt32(bytes, record);
            var tableOffset = UInt32(bytes, record + 8);
            var length = UInt32(bytes, record + 12);
            // SFNT table offsets in TTC files are relative to the whole collection.
            if ((ulong)tableOffset + length > (ulong)bytes.Length || !tags.Add(tag))
                throw Invalid(assetPath, sourceName, $"face {face} has an out-of-bounds or duplicate SFNT table");
            if (tag == SvgTag) svg = true;
            if (tag == ColrTag) { colrOffset = tableOffset; colrLength = length; }
        }
        if (svg) throw Unsupported(assetPath, sourceName, face, "SVG glyphs");
        if (!tags.Contains(ColrTag)) return;
        if (colrLength < 2)
            throw Invalid(assetPath, sourceName, $"face {face} has a truncated COLR header");
        var colr = bytes.Slice((int)colrOffset, (int)colrLength);
        var colrVersion = UInt16(colr, 0);
        if (colrVersion != 0) throw Unsupported(assetPath, sourceName, face, $"COLR version {colrVersion}");
        if (colr.Length < 14)
            throw Invalid(assetPath, sourceName, $"face {face} has a truncated COLRv0 header");
        var baseCount = UInt16(colr, 2);
        var baseOffset = UInt32(colr, 4);
        var layerOffset = UInt32(colr, 8);
        var layerCount = UInt16(colr, 12);
        if (!ValidRecords(baseOffset, baseCount, 6, colr.Length) ||
            !ValidRecords(layerOffset, layerCount, 4, colr.Length))
            throw Invalid(assetPath, sourceName, $"face {face} has invalid COLRv0 record ranges");
        for (var index = 0; index < baseCount; index++)
        {
            var record = (int)baseOffset + index * 6;
            if ((uint)UInt16(colr, record + 2) + UInt16(colr, record + 4) > layerCount)
                throw Invalid(assetPath, sourceName, $"face {face} has an invalid COLRv0 layer range");
        }
    }

    private static bool ValidRecords(uint offset, ushort count, int size, int length) =>
        count == 0 ? offset <= length : offset >= 14 && (ulong)offset + (uint)count * (uint)size <= (ulong)length;

    private static ushort UInt16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
    private static uint UInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
    private static InvalidDataException Invalid(string asset, string source, string reason) =>
        new($"Invalid embedded font '{source}' in '{asset}' for Web FreeType: {reason}.");
    private static InvalidDataException Unsupported(string asset, string source, int face, string feature) =>
        new($"Web FreeType cannot render {feature} in embedded font '{source}' (face {face}) from '{asset}'. " +
            "Use Skia in Player Settings > Web Font Backend, or replace the font.");
}
