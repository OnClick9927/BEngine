namespace BEngine.IO;

/// <summary>
/// Exposes the bytes after a fixed offset as an independently seekable stream.
/// The implementation lives in the AOT host assembly so interpreted packages can use it
/// without asking an AOT runtime to build virtual dispatch stubs for a package-defined Stream.
/// </summary>
public sealed class OffsetReadStream : Stream
{
    private readonly Stream _source;
    private readonly long _offset;
    private readonly bool _leaveOpen;
    private long _position;

    public OffsetReadStream(Stream source, long offset, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("The source stream must be readable and seekable.", nameof(source));
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        _source = source;
        _offset = offset;
        _leaveOpen = leaveOpen;
    }

    public override bool CanRead => _source.CanRead;
    public override bool CanSeek => _source.CanSeek;
    public override bool CanWrite => false;
    public override long Length => Math.Max(0, _source.Length - _offset);

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

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        _source.Position = checked(_offset + _position);
        var read = _source.Read(buffer);
        _position += read;
        return read;
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
        if (disposing && !_leaveOpen)
            _source.Dispose();
        base.Dispose(disposing);
    }
}
