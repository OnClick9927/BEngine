using BEngine.Platform;

namespace BEngine.Audio;

public sealed class IosAudioOutputFactory : IAudioOutputFactory
{
    public static IosAudioOutputFactory Shared { get; } = new();

    public IAudioOutput CreateDefault() =>
        IosAudioOutput.TryCreate(out var output) ? output! : OpenAlAudioOutputFactory.Shared.SilentOutput;
    public IAudioOutput SilentOutput => OpenAlAudioOutputFactory.Shared.SilentOutput;
    public bool IsSystemOutput(IAudioOutput output) => output is IosAudioOutput;
}

public sealed class IosAudioOutput : IAudioOutput, IAudioOutputDiagnostics, IDisposable
{
    private readonly IPlatformAudioOutput _output;

    private IosAudioOutput(IPlatformAudioOutput output) => _output = output;

    public int sampleRate => _output.SampleRate;
    public int channelCount => _output.ChannelCount;
    public string backend => _output.Backend;
    public long submittedFrames => _output.SubmittedFrames;
    public long completedFrames => _output.CompletedFrames;
    public long droppedFrames => _output.DroppedFrames;
    public int pendingBuffers => _output.PendingBuffers;

    public static bool TryCreate(out IosAudioOutput? output, int sampleRate = 48000, int channelCount = 2)
    {
        output = null;
        if (!OperatingSystem.IsIOS()) return false;
        try
        {
            var platformOutput = PlatformRuntime.GetService<IPlatformAudioOutputFactory>()?
                .CreateDefault(sampleRate, channelCount);
            if (platformOutput is null) return false;
            output = new IosAudioOutput(platformOutput);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"iOS AudioQueue output is unavailable: {exception.Message}");
            output?.Dispose();
            output = null;
            return false;
        }
    }

    public void Submit(ReadOnlySpan<float> interleavedSamples) => _output.Submit(interleavedSamples);

    public void Dispose()
    {
        _output.Dispose();
        GC.SuppressFinalize(this);
    }
}
