namespace BEngine.Platform;

/// <summary>Platform container decoding, supplied by a native Player host. Pixels are top-down RGBA.</summary>
public interface IPlatformVideoFrameReader : IDisposable
{
    int Width { get; }
    int Height { get; }
    double FrameRate { get; }
    double Duration { get; }
    double Timestamp { get; }
    string Backend { get; }
    bool HasAudio { get; }
    bool RequiresPolling => false;
    bool HasPendingFrame => false;
    IPlatformVideoFrameReader Clone() => throw new NotSupportedException("This native reader does not support synchronous cloning.");
    IPlatformAudioSampleReader? OpenAudio() => null;
    void ReadFrame(double seconds, Span<byte> rgba);
}
public static class PlatformVideoDecoding
{
    public static Func<string, ReadOnlyMemory<byte>, bool, IPlatformVideoFrameReader>? OpenVideo { get; set; }
    public static Func<string, ReadOnlyMemory<byte>, IPlatformAudioSampleReader>? OpenAudio { get; set; }
    public static Func<string, ReadOnlyMemory<byte>, bool, CancellationToken, Task<IPlatformVideoFrameReader>>? OpenVideoAsync { get; set; }
}

/// <summary>Container audio decoded by the static platform host for the engine mixer.</summary>
public interface IPlatformAudioSampleReader : IDisposable
{
    int Channels { get; }
    int Frequency { get; }
    int Samples { get; }
    int Position { get; }
    /// <summary>A zero-length read is temporary while asynchronous samples are being fetched.</summary>
    bool HasPendingSamples => false;
    int Read(float[] buffer, int offset, int count);
    void Seek(int frame);
}
