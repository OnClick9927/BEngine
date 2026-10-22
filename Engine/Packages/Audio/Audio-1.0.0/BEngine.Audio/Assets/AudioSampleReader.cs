namespace BEngine.Audio;

/// <summary>An owned, independently seekable audio stream. Read counts are interleaved samples;
/// samples, position and Seek use frames (one sample per channel).</summary>
public abstract class AudioSampleReader : IDisposable
{
    private bool _disposed;
    public abstract int channels { get; }
    public abstract int frequency { get; }
    public abstract int samples { get; }
    public abstract int position { get; }
    public virtual bool hasPendingSamples => false;
    public int Read(float[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count || channels <= 0 || count % channels != 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        var read = ReadCore(buffer, offset, count);
        if (read < 0 || read > count || read % channels != 0) throw new InvalidDataException("Decoder returned an invalid sample count.");
        using var budget = LoopExecutionGuard.AllowIterations(read + 1);
        for (var i = offset; i < offset + read; i++) buffer[i] = float.IsFinite(buffer[i]) ? Math.Clamp(buffer[i], -1, 1) : 0;
        return read;
    }
    public void Seek(int frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (frame < 0 || frame > samples) throw new ArgumentOutOfRangeException(nameof(frame));
        SeekCore(frame);
    }
    protected abstract int ReadCore(float[] buffer, int offset, int count);
    protected abstract void SeekCore(int frame);
    protected virtual void DisposeCore() { }
    public void Dispose() { if (_disposed) return; _disposed = true; DisposeCore(); GC.SuppressFinalize(this); }
}

public enum AudioClipLoadType { DecompressOnLoad, Streaming }
