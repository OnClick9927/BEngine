using BEngine.Audio;
using BEngine.Platform;

namespace BEngine.Video;

public class PlatformVideoAudioReader : AudioSampleReader
{
    private readonly IPlatformAudioSampleReader native;
    public PlatformVideoAudioReader(IPlatformAudioSampleReader native)
    { ArgumentNullException.ThrowIfNull(native); this.native = native; }
    public override int channels => native.Channels;
    public override int frequency => native.Frequency;
    public override int samples => native.Samples;
    public override int position => native.Position;
    public override bool hasPendingSamples => native.HasPendingSamples;
    protected override int ReadCore(float[] buffer, int offset, int count) => native.Read(buffer, offset, count);
    protected override void SeekCore(int frame) => native.Seek(frame);
    protected override void DisposeCore() => native.Dispose();
}
