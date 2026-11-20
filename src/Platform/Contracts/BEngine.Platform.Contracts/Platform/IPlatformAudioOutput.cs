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
    /// <summary>Null when the device cannot observe queue starvation.</summary>
    long? UnderrunEvents => null;
    /// <summary>Device-clock scheduled latency, when the backend can measure it.</summary>
    double? BufferedMilliseconds => null;

    void Submit(ReadOnlySpan<float> interleavedSamples);
}

public interface IPlatformAudioOutputFactory
{
    IPlatformAudioOutput? CreateDefault(int sampleRate, int channelCount);
    IPlatformAudioOutput? CreateDefault(PlatformAudioOutputOptions options) =>
        CreateDefault(options.SampleRate, options.ChannelCount);
}

/// <summary>Requested device format and queue budget. The consumer chooses policy; the host owns the device.</summary>
public readonly record struct PlatformAudioOutputOptions
{
    public int SampleRate { get; }
    public int ChannelCount { get; }
    public int BufferMilliseconds { get; }

    public PlatformAudioOutputOptions(int sampleRate, int channelCount, int bufferMilliseconds = 120)
    {
        if (sampleRate is < 8000 or > 384000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channelCount is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(channelCount));
        if (bufferMilliseconds is < 20 or > 2000) throw new ArgumentOutOfRangeException(nameof(bufferMilliseconds));
        SampleRate = sampleRate;
        ChannelCount = channelCount;
        BufferMilliseconds = bufferMilliseconds;
    }
}

/// <summary>Optional bounded native queue, including frames already submitted to the device.</summary>
public interface IPlatformBufferedAudioOutput : IPlatformAudioOutput
{
    int CapacityFrames { get; }
    int QueuedFrames { get; }
    int AvailableFrames { get; }
    void SetExpectedSubmissionFrames(int frames)
    { if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames)); }
    bool TrySubmit(ReadOnlySpan<float> samples, out int acceptedFrames);
}

/// <summary>Opts into queue operations from one producer thread and disposal after that producer has stopped.</summary>
public interface IPlatformIndependentAudioOutput : IPlatformBufferedAudioOutput { }
