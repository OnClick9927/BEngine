using System.Buffers.Binary;
using System.Text;

namespace BEngine.UIElements;

// JSON remains the authoring format. Runtime data stores the same fields without
// JSON reflection, base64 expansion or culture-dependent numeric conversion.
internal static class SdfFontBinaryCodec
{
    internal const int MaximumDocumentBytes = 192 * 1024 * 1024;
    private const int HeaderSize = 64;
    private const int GlyphSize = 32;
    private const int MaximumFamilyBytes = 1024 * 1024;
    private const int MaximumSourceNameBytes = 1024;
    private const int MaximumSourceBytes = 64 * 1024 * 1024;
    private const int MaximumTotalSourceBytes = 128 * 1024 * 1024;
    private const ushort Version = 1;
    private static ReadOnlySpan<byte> Magic => "BENGSDF\0"u8;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static bool IsBinary(ReadOnlySpan<byte> bytes) => bytes.StartsWith(Magic);

    internal static byte[] Write(SdfFontData data)
    {
        data.Validate();
        var family = Encode(data.FontFamily, MaximumFamilyBytes);
        var names = data.SourceFonts.Select(source => Encode(source.Name, MaximumSourceNameBytes)).ToArray();
        long length = HeaderSize + (long)family.Length + data.Pixels.Length + (long)data.Glyphs.Length * GlyphSize;
        for (var index = 0; index < names.Length; index++)
            length += 8L + names[index].Length + data.SourceFonts[index].Data.Length;
        if (length > MaximumDocumentBytes) throw Invalid("Runtime font exceeds the document size limit.");
        using var stream = new MemoryStream((int)length);
        using var writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write((ushort)0);
        writer.Write((int)length);
        writer.Write(data.Version);
        writer.Write(data.SamplingSize);
        writer.Write(data.LineHeight);
        writer.Write(data.DistanceRange);
        writer.Write(data.Width);
        writer.Write(data.Height);
        writer.Write(family.Length);
        writer.Write(data.Pixels.Length);
        writer.Write(data.Glyphs.Length);
        writer.Write(data.SourceFonts.Length);
        writer.Write(0L);
        writer.Write(family);
        writer.Write(data.Pixels);
        foreach (var glyph in data.Glyphs)
        {
            writer.Write(glyph.Codepoint); writer.Write(glyph.X); writer.Write(glyph.Y);
            writer.Write(glyph.Width); writer.Write(glyph.Height);
            writer.Write(glyph.Advance); writer.Write(glyph.BearingX); writer.Write(glyph.BearingY);
        }
        for (var index = 0; index < names.Length; index++)
        {
            writer.Write(names[index].Length);
            writer.Write(data.SourceFonts[index].Data.Length);
            writer.Write(names[index]);
            writer.Write(data.SourceFonts[index].Data);
        }
        writer.Flush();
        return stream.ToArray();
    }

    internal static SdfFontData Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < HeaderSize or > MaximumDocumentBytes || !IsBinary(bytes))
            throw Invalid("Invalid runtime font header.");
        var reader = new Reader(bytes);
        reader.Skip(Magic.Length);
        if (reader.UInt16() != Version || reader.UInt16() != 0 || reader.Int32() != bytes.Length)
            throw Invalid("Unsupported runtime font version, flags or size.");
        var data = new SdfFontData
        {
            Version = reader.Int32(), SamplingSize = reader.Int32(), LineHeight = reader.Single(),
            DistanceRange = reader.Int32(), Width = reader.Int32(), Height = reader.Int32()
        };
        var familyLength = reader.Count(0, MaximumFamilyBytes);
        var pixelCount = reader.Count(1, 4096 * 4096);
        var glyphCount = reader.Count(2, 8192);
        var sourceCount = reader.Count(0, 32);
        if (reader.Int32() != 0 || reader.Int32() != 0 ||
            data.Version is not (1 or 2) || data.SamplingSize is < 16 or > 256 ||
            !float.IsFinite(data.LineHeight) || data.LineHeight <= 0 || data.LineHeight > 1024 ||
            data.DistanceRange is < 2 or > 32 || data.Width is < 1 or > 4096 || data.Height is < 1 or > 4096 ||
            (long)data.Width * data.Height != pixelCount ||
            data.Version == 1 && sourceCount != 0 || data.Version == 2 && sourceCount == 0)
            throw Invalid("Invalid runtime font dimensions, source count or reserved fields.");
        if (familyLength + (long)pixelCount + (long)glyphCount * GlyphSize + sourceCount * 8L > reader.Remaining)
            throw Invalid("Runtime font fields exceed the remaining document.");
        data.FontFamily = reader.String(familyLength);
        data.Pixels = reader.Bytes(pixelCount);
        var glyphs = new SdfGlyph[glyphCount];
        for (var index = 0; index < glyphs.Length; index++)
            glyphs[index] = new(reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32(),
                reader.Single(), reader.Single(), reader.Single());
        data.Glyphs = glyphs;
        var sources = new DynamicFontSource[sourceCount];
        long totalSourceBytes = 0;
        for (var index = 0; index < sources.Length; index++)
        {
            var nameLength = reader.Count(1, MaximumSourceNameBytes);
            var sourceLength = reader.Count(12, MaximumSourceBytes);
            totalSourceBytes += sourceLength;
            if (totalSourceBytes > MaximumTotalSourceBytes || nameLength + (long)sourceLength > reader.Remaining)
                throw Invalid("Runtime embedded font data exceeds its bounds.");
            var name = reader.String(nameLength);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 256)
                throw Invalid("Invalid runtime embedded font name.");
            sources[index] = new(name, reader.Bytes(sourceLength));
        }
        if (reader.Remaining != 0) throw Invalid("Runtime font contains trailing bytes.");
        data.SourceFonts = sources;
        return data;
    }

    private static byte[] Encode(string value, int maximumBytes)
    {
        try
        {
            if (StrictUtf8.GetByteCount(value) > maximumBytes) throw Invalid("Runtime font string exceeds its limit.");
            return StrictUtf8.GetBytes(value);
        }
        catch (EncoderFallbackException error) { throw new InvalidDataException("Runtime font string is not valid Unicode.", error); }
    }

    private static InvalidDataException Invalid(string message) => new(message);

    private ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private ReadOnlySpan<byte> _remaining = bytes;
        internal int Remaining => _remaining.Length;
        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > _remaining.Length) throw Invalid("Runtime font is truncated.");
            var result = _remaining[..count]; _remaining = _remaining[count..]; return result;
        }
        internal void Skip(int count) => Take(count);
        internal ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        internal int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        internal float Single() => BitConverter.Int32BitsToSingle(Int32());
        internal int Count(int minimum, int maximum)
        {
            var count = Int32();
            if (count < minimum || count > maximum) throw Invalid("Runtime font count exceeds its bounds.");
            return count;
        }
        internal byte[] Bytes(int count) => Take(count).ToArray();
        internal string String(int count)
        {
            try { return StrictUtf8.GetString(Take(count)); }
            catch (DecoderFallbackException error) { throw new InvalidDataException("Runtime font string is not valid UTF-8.", error); }
        }
    }
}
