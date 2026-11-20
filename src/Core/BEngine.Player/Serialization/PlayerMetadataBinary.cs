using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace BEngine.Player.Serialization;

/// <summary>Bounded field encoding shared by Player metadata and content-owned sections.</summary>
public static class PlayerMetadataBinary
{
    public const int MaximumStringBytes = 64 * 1024;
    public const int MaximumCollectionItems = 4096;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    /// <summary>Borrows an array-backed section while the reader is alive; other memory is copied.</summary>
    public static BinaryReader CreateReader(ReadOnlyMemory<byte> data)
    {
        if (!MemoryMarshal.TryGetArray(data, out var segment)) segment = new(data.ToArray());
        return new BinaryReader(new MemoryStream(segment.Array!, segment.Offset, segment.Count,
            writable: false, publiclyVisible: true), Encoding.UTF8, leaveOpen: false);
    }

    public static void WriteStrings(BinaryWriter writer, IReadOnlyCollection<string> values)
    {
        WriteCount(writer, values.Count, "string");
        foreach (var value in values) WriteString(writer, value);
    }

    public static List<string> ReadStrings(BinaryReader reader)
    {
        var count = ReadCount(reader, "string");
        var result = new List<string>(count);
        for (var index = 0; index < count; index++) result.Add(ReadString(reader));
        return result;
    }

    public static void WriteString(BinaryWriter writer, string value)
    {
        if (value is null) throw new InvalidDataException("Runtime metadata strings cannot be null.");
        var length = Encoding.UTF8.GetByteCount(value);
        if (length > MaximumStringBytes)
            throw new InvalidDataException("Runtime metadata string exceeds the maximum supported length.");
        writer.Write(length);
        if (length == 0) return;
        byte[]? rented = null;
        Span<byte> bytes = length <= 512 ? stackalloc byte[length] : (rented = ArrayPool<byte>.Shared.Rent(length));
        try
        {
            Encoding.UTF8.GetBytes(value.AsSpan(), bytes);
            writer.Write(bytes[..length]);
        }
        finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented); }
    }

    public static string ReadString(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var length = reader.ReadInt32();
        if (length is < 0 or > MaximumStringBytes || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Runtime metadata string length is invalid.");
        if (length == 0) return string.Empty;
        var stream = reader.BaseStream;
        byte[]? rented = null;
        try
        {
            if (stream is MemoryStream memory && memory.TryGetBuffer(out var buffer))
            {
                var bytes = buffer.AsSpan(checked((int)stream.Position), length);
                stream.Position += length;
                return StrictUtf8.GetString(bytes);
            }
            rented = ArrayPool<byte>.Shared.Rent(length);
            stream.ReadExactly(rented.AsSpan(0, length));
            return StrictUtf8.GetString(rented.AsSpan(0, length));
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Runtime metadata contains invalid UTF-8.", exception);
        }
        finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented); }
    }

    public static void WriteBoolean(BinaryWriter writer, bool value) => writer.Write(value ? (byte)1 : (byte)0);

    public static bool ReadBoolean(BinaryReader reader) => reader.ReadByte() switch
    {
        0 => false,
        1 => true,
        _ => throw new InvalidDataException("Runtime metadata boolean value is invalid.")
    };

    public static void WriteCount(BinaryWriter writer, int count, string description)
    {
        if (count is < 0 or > MaximumCollectionItems)
            throw new InvalidDataException(
                $"Runtime metadata {description} count exceeds the supported range.");
        writer.Write(count);
    }

    public static int ReadCount(
        BinaryReader reader,
        string description,
        int maximum = MaximumCollectionItems)
    {
        var count = reader.ReadInt32();
        if (count is < 0 || count > maximum)
            throw new InvalidDataException($"Runtime metadata {description} count is invalid.");
        return count;
    }

}
