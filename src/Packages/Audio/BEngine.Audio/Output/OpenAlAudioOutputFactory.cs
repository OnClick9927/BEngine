namespace BEngine.Audio;

public sealed class OpenAlAudioOutputFactory : IAudioOutputFactory
{
    public static OpenAlAudioOutputFactory Shared { get; } = new();

    public IAudioOutput CreateDefault() =>
        OpenAlAudioOutput.TryCreate(out var output) ? output! : SilentAudioOutput.Instance;

    public IAudioOutput SilentOutput => SilentAudioOutput.Instance;

    public bool IsSystemOutput(IAudioOutput output) => output is OpenAlAudioOutput;

    private sealed class SilentAudioOutput : IAudioOutput
    {
        internal static SilentAudioOutput Instance { get; } = new();

        public int sampleRate => 48000;
        public int channelCount => 2;

        public void Submit(ReadOnlySpan<float> interleavedSamples) { }
    }
}
