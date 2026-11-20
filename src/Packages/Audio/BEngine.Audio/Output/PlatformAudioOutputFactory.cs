using BEngine.Platform;

namespace BEngine.Audio;

/// <summary>Adapts the registered host output without selecting an operating system or native backend.</summary>
public sealed class PlatformAudioOutputFactory : IAudioOutputFactory
{
    public static PlatformAudioOutputFactory Shared { get; } = new();

    public IAudioOutput CreateDefault()
    {
        var native = PlatformRuntime.GetService<IPlatformAudioOutputFactory>()?.CreateDefault(
            new PlatformAudioOutputOptions(48000, 2, AudioRuntimeSettings.outputBufferMilliseconds));
        return native switch
        {
            IPlatformIndependentAudioOutput independent => new IndependentOutputAdapter(independent),
            IPlatformBufferedAudioOutput buffered => new BufferedOutputAdapter(buffered),
            not null => new OutputAdapter(native),
            _ => SilentAudioOutput.Instance
        };
    }

    public IAudioOutput SilentOutput => SilentAudioOutput.Instance;
    public bool IsSystemOutput(IAudioOutput output) => output is OutputAdapter;

    private class OutputAdapter(IPlatformAudioOutput output) : IAudioOutput, IAudioOutputDiagnostics, IDisposable
    {
        public int sampleRate => output.SampleRate;
        public int channelCount => output.ChannelCount;
        public string backend => output.Backend;
        public long submittedFrames => output.SubmittedFrames;
        public long completedFrames => output.CompletedFrames;
        public long droppedFrames => output.DroppedFrames;
        public int pendingBuffers => output.PendingBuffers;
        public long? underrunEvents => output.UnderrunEvents;
        public double? bufferedMilliseconds => output.BufferedMilliseconds;
        public void Submit(ReadOnlySpan<float> samples) => output.Submit(samples);
        public void Dispose() => output.Dispose();
    }

    private class BufferedOutputAdapter(IPlatformBufferedAudioOutput output) : OutputAdapter(output), IAudioBufferedOutput
    {
        public int capacityFrames => output.CapacityFrames;
        public int queuedFrames => output.QueuedFrames;
        public int availableFrames => output.AvailableFrames;
        public void SetExpectedSubmissionFrames(int frames) => output.SetExpectedSubmissionFrames(frames);
        public bool TrySubmit(ReadOnlySpan<float> samples, out int acceptedFrames) => output.TrySubmit(samples, out acceptedFrames);
    }

    private sealed class IndependentOutputAdapter(IPlatformIndependentAudioOutput output) : BufferedOutputAdapter(output), IIndependentAudioOutput;

    private sealed class SilentAudioOutput : IAudioOutput
    {
        internal static SilentAudioOutput Instance { get; } = new();
        public int sampleRate => 48000;
        public int channelCount => 2;
        public void Submit(ReadOnlySpan<float> samples) { }
    }
}
