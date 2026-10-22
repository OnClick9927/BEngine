namespace BEngine.Platform;

/// <summary>Places decoded PCM packets on the media timeline, including seek preroll and gaps.</summary>
public abstract class TimestampedPcmReader : IPlatformAudioSampleReader
{
    private float[] _block = [];
    private int _blockStart;
    private bool _ended, _disposed;
    public abstract int Channels { get; }
    public abstract int Frequency { get; }
    public abstract int Samples { get; }
    public int Position { get; private set; }
    protected abstract bool ReadPacket(out int startFrame, out float[] samples);
    protected abstract void SeekDecoder(int frame);
    protected abstract void DisposeDecoder();
    public int Read(float[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(buffer);
        if (Channels is < 1 or > 8 || Frequency <= 0 || Samples < 0 || offset < 0 || count < 0 || offset > buffer.Length - count || count % Channels != 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        var frames = Math.Min(count / Channels, Samples - Position); var written = 0; var packets = 0;
        while (frames > 0)
        {
            while (!_ended && (long)_blockStart + _block.Length / Channels <= Position)
            {
                if (++packets > 65536) throw new InvalidDataException("Container audio exceeded its packet decode budget.");
                _ended = !ReadPacket(out _blockStart, out _block);
                if (_block.Length % Channels != 0 || _block.Length > 16 * 1024 * 1024) throw new InvalidDataException("Invalid platform PCM packet.");
            }
            var silence = _ended || Position < _blockStart;
            var take = silence ? _ended ? frames : Math.Min(frames, _blockStart - Position) : (int)Math.Min(frames, (long)_blockStart + _block.Length / Channels - Position);
            if (silence) Array.Clear(buffer, offset + written, take * Channels);
            else Array.Copy(_block, (Position - _blockStart) * Channels, buffer, offset + written, take * Channels);
            written += take * Channels; Position += take; frames -= take;
        }
        for (var index = offset; index < offset + written; index++) buffer[index] = float.IsFinite(buffer[index]) ? Math.Clamp(buffer[index], -1, 1) : 0;
        return written;
    }
    public void Seek(int frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (frame < 0 || frame > Samples) throw new ArgumentOutOfRangeException(nameof(frame));
        SeekDecoder(frame); Position = frame; _block = []; _blockStart = frame; _ended = false;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _block = []; DisposeDecoder(); GC.SuppressFinalize(this);
    }
}
