using System.Buffers.Binary;
using System.Security.Cryptography;

namespace BEngine.Editor;

// Runtime bundle delta format: BDLT1, source length, target length, source SHA256,
// target SHA256, then unsigned varint commands. Low two tag bits select
// literal(0), source-copy(1), zero(2); high bits contain a positive length.
// A copy's tag is followed by its unsigned source offset.
internal static class WebRuntimeBinaryDelta
{
    static ReadOnlySpan<byte> Magic => "BDLT1"u8;
    const int MinimumCopy = 8;
    const int MaximumCandidates = 64;

    public static byte[] Create(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target)
    {
        if (source.Length > 0x3fffffff || target.Length > 0x3fffffff)
            throw new ArgumentOutOfRangeException(nameof(target), "Binary delta inputs exceed the format's per-command length limit.");
        var heads = new Dictionary<ulong, int>();
        var previous = new int[Math.Max(0, source.Length - MinimumCopy + 1)];
        for (int i = 0; i < previous.Length; ++i)
        {
            ulong key = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(i, MinimumCopy));
            previous[i] = heads.TryGetValue(key, out int last) ? last : -1;
            heads[key] = i;
        }

        using var stream = new MemoryStream();
        stream.Write(Magic);
        WriteVarUInt(stream, (uint)source.Length);
        WriteVarUInt(stream, (uint)target.Length);
        stream.Write(SHA256.HashData(source));
        stream.Write(SHA256.HashData(target));
        int literalStart = 0, cursor = 0;
        while (cursor < target.Length)
        {
            int zeroLength = 0;
            if (target[cursor] == 0)
            {
                while (cursor + zeroLength < target.Length && target[cursor + zeroLength] == 0)
                    ++zeroLength;
            }
            int matchLength = 0, matchOffset = 0;
            if (cursor <= target.Length - MinimumCopy)
            {
                ulong key = BinaryPrimitives.ReadUInt64LittleEndian(target.Slice(cursor, MinimumCopy));
                if (heads.TryGetValue(key, out int candidate))
                {
                    for (int scanned = 0; candidate >= 0 && scanned < MaximumCandidates; ++scanned, candidate = previous[candidate])
                    {
                        int maximumLength = Math.Min(source.Length - candidate, target.Length - cursor);
                        if (maximumLength <= matchLength) continue;
                        int length = MinimumCopy;
                        while (length <= maximumLength - sizeof(ulong) &&
                               BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(candidate + length, sizeof(ulong))) ==
                               BinaryPrimitives.ReadUInt64LittleEndian(target.Slice(cursor + length, sizeof(ulong))))
                            length += sizeof(ulong);
                        while (length < maximumLength && source[candidate + length] == target[cursor + length]) ++length;
                        if (length > matchLength) { matchLength = length; matchOffset = candidate; }
                    }
                }
            }
            bool useZero = zeroLength >= MinimumCopy && zeroLength >= matchLength;
            int operationLength = useZero ? zeroLength : matchLength;
            if (operationLength < MinimumCopy) { ++cursor; continue; }
            WriteLiteral(stream, target.Slice(literalStart, cursor - literalStart));
            WriteVarUInt(stream, checked(((uint)operationLength << 2) | (useZero ? 2u : 1u)));
            if (!useZero) WriteVarUInt(stream, (uint)matchOffset);
            cursor += operationLength;
            literalStart = cursor;
        }
        WriteLiteral(stream, target[literalStart..]);
        return stream.ToArray();
    }

    public static byte[] Apply(ReadOnlySpan<byte> source, ReadOnlySpan<byte> delta, int maximumOutputLength = 256 * 1024 * 1024)
    {
        if (!delta.StartsWith(Magic)) throw new InvalidDataException("Invalid binary delta magic.");
        int cursor = Magic.Length;
        uint sourceLength = ReadVarUInt(delta, ref cursor);
        uint outputLength = ReadVarUInt(delta, ref cursor);
        if (sourceLength != source.Length) throw new InvalidDataException("Binary delta source length mismatch.");
        if (maximumOutputLength < 0 || outputLength > maximumOutputLength) throw new InvalidDataException("Binary delta output exceeds its configured limit.");
        if (delta.Length - cursor < 64) throw new InvalidDataException("Truncated binary delta hashes.");
        ReadOnlySpan<byte> sourceHash = delta.Slice(cursor, 32), outputHash = delta.Slice(cursor + 32, 32);
        cursor += 64;
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(source), sourceHash))
            throw new InvalidDataException("Binary delta source SHA256 mismatch.");
        byte[] output = new byte[(int)outputLength];
        int written = 0;
        while (written < output.Length)
        {
            uint tag = ReadVarUInt(delta, ref cursor);
            uint length = tag >> 2, kind = tag & 3;
            if (length == 0 || length > output.Length - written) throw new InvalidDataException("Invalid binary delta output range.");
            switch (kind)
            {
                case 0:
                    if (length > delta.Length - cursor) throw new InvalidDataException("Truncated binary delta literal.");
                    delta.Slice(cursor, (int)length).CopyTo(output.AsSpan(written));
                    cursor += (int)length;
                    break;
                case 1:
                    uint offset = ReadVarUInt(delta, ref cursor);
                    if (offset > source.Length || length > source.Length - offset) throw new InvalidDataException("Invalid binary delta source range.");
                    source.Slice((int)offset, (int)length).CopyTo(output.AsSpan(written));
                    break;
                case 2:
                    break; // Fresh output is already zero initialized.
                default:
                    throw new InvalidDataException("Unknown binary delta command.");
            }
            written += (int)length;
        }
        if (cursor != delta.Length) throw new InvalidDataException("Trailing binary delta data.");
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(output), outputHash))
            throw new InvalidDataException("Binary delta output SHA256 mismatch.");
        return output;
    }

    static void WriteLiteral(Stream stream, ReadOnlySpan<byte> literal)
    {
        if (literal.IsEmpty) return;
        WriteVarUInt(stream, checked((uint)literal.Length << 2));
        stream.Write(literal);
    }

    static void WriteVarUInt(Stream stream, uint value)
    {
        while (value >= 0x80) { stream.WriteByte((byte)(value | 0x80)); value >>= 7; }
        stream.WriteByte((byte)value);
    }

    static uint ReadVarUInt(ReadOnlySpan<byte> source, ref int cursor)
    {
        uint value = 0;
        for (int index = 0; index < 5; ++index)
        {
            if (cursor == source.Length) throw new InvalidDataException("Truncated binary delta integer.");
            byte item = source[cursor++];
            if (index == 4 && (item & 0xf0) != 0) throw new InvalidDataException("Overflowed binary delta integer.");
            value |= (uint)(item & 0x7f) << (index * 7);
            if ((item & 0x80) == 0)
            {
                if (index > 0 && item == 0) throw new InvalidDataException("Non-canonical binary delta integer.");
                return value;
            }
        }
        throw new InvalidDataException("Overflowed binary delta integer.");
    }
}
