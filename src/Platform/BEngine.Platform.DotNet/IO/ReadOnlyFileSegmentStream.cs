using Microsoft.Win32.SafeHandles;

namespace BEngine.Platform;

/// <summary>Seekable view of an uncompressed asset inside a package. Reads never leave its byte range.</summary>
public sealed class ReadOnlyFileSegmentStream : Stream
{
    private readonly SafeFileHandle _handle;
    private readonly IDisposable _owner;
    private readonly long _offset;
    private readonly long _length;
    private long _position;
    private bool _disposed;

    public ReadOnlyFileSegmentStream(SafeFileHandle handle, long offset, long length, IDisposable owner)
    {
        _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        if (offset < 0 || length < 0 || offset > long.MaxValue - length)
            throw new ArgumentOutOfRangeException(nameof(length));
        _offset = offset; _length = length;
    }
    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => false;
    public override long Length { get { ObjectDisposedException.ThrowIf(_disposed, this); return _length; } }
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var count = (int)Math.Min(buffer.Length, _length - _position);
        if (count == 0) return 0;
        var read = RandomAccess.Read(_handle, buffer[..count], _offset + _position);
        if (read == 0) throw new EndOfStreamException("The packaged asset ended before its declared length.");
        _position += read;
        return read;
    }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(Read(buffer.Span)); }
    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var next = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(_position + offset),
            SeekOrigin.End => checked(_length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (next < 0 || next > _length) throw new IOException("Seek is outside the packaged asset.");
        return _position = next;
    }
    public override void Flush() { ObjectDisposedException.ThrowIf(_disposed, this); }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            try { _handle.Dispose(); } finally { _owner.Dispose(); }
        }
        base.Dispose(disposing);
    }
}
