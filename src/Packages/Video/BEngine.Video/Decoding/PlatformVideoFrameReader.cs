using BEngine.Platform;

namespace BEngine.Video;

internal sealed class PlatformVideoFrameReader(IPlatformVideoFrameReader native) : VideoFrameReader
{
    private long _frames;
    public override int width => native.Width;
    public override int height => native.Height;
    public override double frameRate => native.FrameRate;
    public override double duration => native.Duration;
    public override double timestamp => native.Timestamp;
    public override bool canSeek => true;
    public override string backend => native.Backend;
    public override bool hasAudio => native.HasAudio;
    public override bool requiresPolling => native.RequiresPolling;
    public override bool hasPendingFrame => native.HasPendingFrame;
    public override long decodedFrames => _frames;
    protected override void ReadFrameCore(double seconds, Span<byte> rgba) { native.ReadFrame(seconds, rgba); _frames++; }
    protected override void DisposeCore() => native.Dispose();
}
