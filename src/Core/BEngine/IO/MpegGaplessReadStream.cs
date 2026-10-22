using System.Buffers.Binary;

namespace BEngine.IO;

/// <summary>
/// Presents an MPEG stream with its Xing/LAME gapless metadata neutralized while reporting
/// the decoded delay and frame count. It is part of the AOT host contract for audio packages.
/// </summary>
public sealed class MpegGaplessReadStream : Stream
{
    private readonly Stream _source;
    private readonly long _start;
    private readonly byte[] _prefix;
    private long _position;

    private MpegGaplessReadStream(Stream source, long start, byte[] prefix)
    {
        _source = source;
        _start = start;
        _prefix = prefix;
    }

    public static Stream Prepare(Stream source, out int skip, out int frames)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("The MPEG source must be readable and seekable.", nameof(source));

        skip = frames = 0;
        var original = source.Position;
        try
        {
            source.Position = 0;
            Span<byte> header = stackalloc byte[10];
            for (var tag = 0; tag < 16; tag++)
            {
                var start = source.Position;
                if (source.ReadAtLeast(header, 10, false) != 10) return source;
                if (!header[..3].SequenceEqual("ID3"u8))
                {
                    source.Position = start;
                    break;
                }

                if (header[3] is < 2 or > 4 || (header[6] | header[7] | header[8] | header[9]) >= 128)
                    return source;
                var size = (header[6] << 21) | (header[7] << 14) | (header[8] << 7) | header[9];
                var next = start + 10 + size + (header[3] == 4 && (header[5] & 16) != 0 ? 10 : 0);
                if (next > source.Length) return source;
                source.Position = next;
            }

            var audioStart = source.Position;
            Span<byte> word = stackalloc byte[4];
            if (source.ReadAtLeast(word, 4, false) != 4) return source;
            var h = BinaryPrimitives.ReadUInt32BigEndian(word);
            var version = (int)(h >> 19 & 3);
            var layer = (int)(h >> 17 & 3);
            var bitrateIndex = (int)(h >> 12 & 15);
            var rateIndex = (int)(h >> 10 & 3);
            if ((h & 0xffe00000) != 0xffe00000 || version == 1 || layer != 1 || bitrateIndex is 0 or 15 || rateIndex == 3)
                return source;

            int[] rates = [44100, 48000, 32000];
            int[] mpeg1 = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
            int[] mpeg2 = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
            var rate = rates[rateIndex] / (version == 3 ? 1 : version == 2 ? 2 : 4);
            var sizeBytes = (version == 3 ? 144000 : 72000) * (version == 3 ? mpeg1 : mpeg2)[bitrateIndex] / rate + (int)(h >> 9 & 1);
            var prefix = new byte[sizeBytes];
            source.Position = audioStart;
            if (source.ReadAtLeast(prefix, sizeBytes, false) != sizeBytes) return source;
            var mono = (h >> 6 & 3) == 3;
            var index = 4 + (version == 3 ? (mono ? 17 : 32) : (mono ? 9 : 17));
            if (index + 12 > prefix.Length ||
                !(prefix.AsSpan(index, 4).SequenceEqual("Xing"u8) || prefix.AsSpan(index, 4).SequenceEqual("Info"u8)))
                return source;

            var flags = BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(index + 4));
            index += 8;
            if ((flags & 1) == 0 || (flags & ~15u) != 0) return source;
            var count = BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(index));
            index += 4;
            if ((flags & 2) != 0) index += 4;
            if ((flags & 4) != 0) index += 100;
            if ((flags & 8) != 0) index += 4;
            if (index + 24 > prefix.Length) return source;
            var encoder = prefix.AsSpan(index, 4);
            if (!(encoder.SequenceEqual("LAME"u8) || encoder.SequenceEqual("Lavc"u8) || encoder.SequenceEqual("Lavf"u8)) ||
                (prefix[index + 9] & 0xf0) > 0x10)
                return source;

            var delay = prefix[index + 21] * 16 + (prefix[index + 22] >> 4);
            var padding = (prefix[index + 22] & 15) * 256 + prefix[index + 23];
            var raw = (long)count * (version == 3 ? 1152 : 576);
            if (padding < 529 || raw <= delay + padding || raw - delay - padding > int.MaxValue)
                return source;
            skip = delay + 529;
            frames = (int)(raw - delay - padding);
            prefix.AsSpan(index + 21, 3).Clear();
            return new MpegGaplessReadStream(source, audioStart, prefix);
        }
        finally
        {
            source.Position = original;
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _source.Length - _start;

    public override long Position
    {
        get => _position;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var copied = 0;
        if (_position < _prefix.Length)
        {
            copied = Math.Min(buffer.Length, _prefix.Length - (int)_position);
            _prefix.AsSpan((int)_position, copied).CopyTo(buffer);
            _position += copied;
        }

        if (copied < buffer.Length)
        {
            _source.Position = _start + _position;
            var read = _source.Read(buffer[copied..]);
            copied += read;
            _position += read;
        }

        return copied;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var originPosition = origin switch
        {
            SeekOrigin.Begin => 0,
            SeekOrigin.Current => Position,
            SeekOrigin.End => Length,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        return Position = checked(originPosition + offset);
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _source.Dispose();
        base.Dispose(disposing);
    }
}
