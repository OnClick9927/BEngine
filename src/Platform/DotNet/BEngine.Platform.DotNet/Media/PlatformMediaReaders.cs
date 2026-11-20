namespace BEngine.Platform.DotNet;

/// <summary>Validation and lifetime for synchronous host RGBA cursors; no scene or asset policy.</summary>
public abstract class PlatformVideoReader : IPlatformVideoFrameReader
{
    private bool _disposed;
    public abstract int Width { get; }
    public abstract int Height { get; }
    public abstract double FrameRate { get; }
    public abstract double Duration { get; }
    public abstract double Timestamp { get; }
    public abstract bool CanSeek { get; }
    public abstract string Backend { get; }
    public virtual bool HasAudio => false;
    public virtual long DecodedFrames => -1;
    public virtual long HardwareDecodedFrames => 0;
    public virtual int IndexedFrameCount => 0;
    public void ReadFrame(double seconds, Span<byte> rgba)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > Duration) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (rgba.Length != checked(Width * Height * 4)) throw new ArgumentException("The destination must contain exactly one RGBA frame.", nameof(rgba));
        ReadFrameCore(seconds, rgba);
    }
    protected abstract void ReadFrameCore(double seconds, Span<byte> rgba);
    protected virtual void DisposeCore() { }
    public void Dispose() { if (_disposed) return; _disposed = true; DisposeCore(); GC.SuppressFinalize(this); }
}

/// <summary>Host PCM cursor. Audio-domain normalization and mixer policy belong to the consumer.</summary>
public abstract class PlatformMediaAudioReader : IPlatformAudioSampleReader
{
    private bool _disposed;
    public abstract int Channels { get; }
    public abstract int Frequency { get; }
    public abstract int Samples { get; }
    public abstract int Position { get; }
    public int Read(float[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count || Channels <= 0 || count % Channels != 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        var read = ReadCore(buffer, offset, count);
        if (read < 0 || read > count || read % Channels != 0) throw new InvalidDataException("Decoder returned an invalid sample count.");
        return read;
    }
    public void Seek(int frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (frame < 0 || frame > Samples) throw new ArgumentOutOfRangeException(nameof(frame));
        SeekCore(frame);
    }
    protected abstract int ReadCore(float[] buffer, int offset, int count);
    protected abstract void SeekCore(int frame);
    protected virtual void DisposeCore() { }
    public void Dispose() { if (_disposed) return; _disposed = true; DisposeCore(); GC.SuppressFinalize(this); }
}
