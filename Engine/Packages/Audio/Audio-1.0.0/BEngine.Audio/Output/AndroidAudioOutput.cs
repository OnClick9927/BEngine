using BEngine.Platform;

namespace BEngine.Audio;

public sealed class AndroidAudioOutputFactory : IAudioOutputFactory
{
    public static AndroidAudioOutputFactory Shared { get; } = new();
    public IAudioOutput CreateDefault() => AndroidAudioOutput.TryCreate(out var output)
        ? output! : OpenAlAudioOutputFactory.Shared.SilentOutput;
    public IAudioOutput SilentOutput => OpenAlAudioOutputFactory.Shared.SilentOutput;
    public bool IsSystemOutput(IAudioOutput output) => output is AndroidAudioOutput;
}

/// <summary>Package adapter; native AAudio code and its worker live in BEngine.Android.</summary>
public sealed class AndroidAudioOutput : IIndependentAudioOutput, IAudioOutputDiagnostics, IDisposable
{
    private readonly IPlatformBufferedAudioOutput _output;
    private AndroidAudioOutput(IPlatformBufferedAudioOutput output) => _output = output;
    public int sampleRate => _output.SampleRate;
    public int channelCount => _output.ChannelCount;
    public string backend => _output.Backend;
    public long submittedFrames => _output.SubmittedFrames;
    public long completedFrames => _output.CompletedFrames;
    public long droppedFrames => _output.DroppedFrames;
    public int pendingBuffers => _output.PendingBuffers;
    public int capacityFrames => _output.CapacityFrames;
    public int queuedFrames => _output.QueuedFrames;
    public int availableFrames => _output.AvailableFrames;
    public void SetExpectedSubmissionFrames(int frames)
    { if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames)); }
    public bool TrySubmit(ReadOnlySpan<float> samples, out int acceptedFrames) => _output.TrySubmit(samples, out acceptedFrames);
    public void Submit(ReadOnlySpan<float> samples) => _output.Submit(samples);
    public void Dispose() { _output.Dispose(); GC.SuppressFinalize(this); }
    public static bool TryCreate(out AndroidAudioOutput? output, int sampleRate = 48000, int channelCount = 2)
    {
        output = null;
        if (!OperatingSystem.IsAndroid()) return false;
        if (PlatformRuntime.GetService<IPlatformAudioOutputFactory>()?.CreateDefault(sampleRate, channelCount)
            is not IPlatformBufferedAudioOutput native) return false;
        output = new(native);
        return true;
    }
}
