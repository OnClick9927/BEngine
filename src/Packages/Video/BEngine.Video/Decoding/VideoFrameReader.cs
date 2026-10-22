namespace BEngine.Video;

public enum VideoDecodeMode { Automatic, Software, HardwareRequired }

/// <summary>An independently owned video cursor. Times are in seconds; pixels are top-down RGBA.</summary>
public abstract class VideoFrameReader : IDisposable
{
    private bool _disposed;
    public abstract int width { get; }
    public abstract int height { get; }
    public abstract double frameRate { get; }
    public abstract double duration { get; }
    public abstract double timestamp { get; }
    public abstract bool canSeek { get; }
    public abstract string backend { get; }
    public virtual bool hasAudio => false;
    public virtual bool requiresPolling => false;
    public virtual bool hasPendingFrame => false;
    /// <summary>Frames actually received in decoder-owned DXGI surfaces, not just a requested option.</summary>
    public virtual long hardwareDecodedFrames => 0;
    public virtual long decodedFrames => 0;
    public void ReadFrame(double seconds, Span<byte> rgba)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > duration) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (rgba.Length != checked(width * height * 4)) throw new ArgumentException("The destination must contain exactly one RGBA frame.", nameof(rgba));
        ReadFrameCore(seconds, rgba);
    }
    protected abstract void ReadFrameCore(double seconds, Span<byte> rgba);
    protected virtual void DisposeCore() { }
    public void Dispose() { if (_disposed) return; _disposed = true; DisposeCore(); GC.SuppressFinalize(this); }
}
