namespace BEngine.Platform;

/// <summary>
/// A native audio sink owned by the platform adapter. Native callbacks stay in the
/// ahead-of-time compiled platform assembly instead of a reloadable game package.
/// </summary>
public interface IPlatformAudioOutput : IDisposable
{
    int SampleRate { get; }
    int ChannelCount { get; }
    string Backend { get; }
    long SubmittedFrames { get; }
    long CompletedFrames { get; }
    long DroppedFrames { get; }
    int PendingBuffers { get; }

    void Submit(ReadOnlySpan<float> interleavedSamples);
}

public interface IPlatformAudioOutputFactory
{
    IPlatformAudioOutput? CreateDefault(int sampleRate, int channelCount);
}

/// <summary>Optional bounded native queue, including frames already submitted to the device.</summary>
public interface IPlatformBufferedAudioOutput : IPlatformAudioOutput
{
    int CapacityFrames { get; }
    int QueuedFrames { get; }
    int AvailableFrames { get; }
    bool TrySubmit(ReadOnlySpan<float> samples, out int acceptedFrames);
}
