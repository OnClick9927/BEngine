namespace BEngine.Audio;

public interface IAudioOutput
{
    int sampleRate { get; }
    int channelCount { get; }
    void Submit(ReadOnlySpan<float> interleavedSamples);
}
