using BEngine.Platform;

namespace BEngine.Video;

public class PlatformVideoFrameReader : VideoFrameReader
{
    private readonly IPlatformVideoFrameReader native;
    public PlatformVideoFrameReader(IPlatformVideoFrameReader native)
    { ArgumentNullException.ThrowIfNull(native); this.native = native; }
    protected IPlatformVideoFrameReader Native => native;
    private long _frames;
    public override int width => native.Width;
    public override int height => native.Height;
    public override double frameRate => native.FrameRate;
    public override double duration => native.Duration;
    public override double timestamp => native.Timestamp;
    public override bool canSeek => native.CanSeek;
    public override string backend => native.Backend;
    public override bool hasAudio => native.HasAudio;
    public override bool requiresPolling => native.RequiresPolling;
    public override bool hasPendingFrame => native.HasPendingFrame;
    public override long decodedFrames => native.DecodedFrames >= 0 ? native.DecodedFrames : _frames;
    public override long hardwareDecodedFrames => native.HardwareDecodedFrames;
    protected override void ReadFrameCore(double seconds, Span<byte> rgba) { native.ReadFrame(seconds, rgba); _frames++; }
    protected override void DisposeCore() => native.Dispose();
}
