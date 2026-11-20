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
    bool CanSeek => true;
    /// <summary>Actual decoded frames, or -1 when the host cannot report this counter.</summary>
    long DecodedFrames => -1;
    long HardwareDecodedFrames => 0;
    int IndexedFrameCount => 0;
    bool RequiresPolling => false;
    bool HasPendingFrame => false;
    IPlatformVideoFrameReader Clone() => throw new NotSupportedException("This native reader does not support synchronous cloning.");
    IPlatformAudioSampleReader? OpenAudio() => null;
    void ReadFrame(double seconds, Span<byte> rgba);
}

public enum PlatformVideoDecodeMode { Automatic, Software, HardwareRequired }

/// <summary>PrepareDimensions runs before allocating managed frame buffers. It may reject admission.</summary>
public sealed record PlatformVideoOpenOptions(PlatformVideoDecodeMode Mode = PlatformVideoDecodeMode.Automatic,
    Action<int, int>? PrepareDimensions = null);

public readonly record struct PlatformVideoDecoderCapabilities(bool Available, bool HttpSources, bool HardwareDecoding);

/// <summary>Host media decoding. Encoded memory is borrowed only for the duration of OpenVideo/OpenAudio.</summary>
public interface IPlatformVideoDecoder
{
    PlatformVideoDecoderCapabilities Capabilities { get; }
    int ActiveSessions => 0;
    IPlatformVideoFrameReader OpenVideo(string source, ReadOnlyMemory<byte> bytes, PlatformVideoOpenOptions options);
    IPlatformAudioSampleReader OpenAudio(string source, ReadOnlyMemory<byte> bytes);
}

/// <summary>Optional external-tool decoder, separate from the host's preferred system decoder.</summary>
public interface IPlatformExternalVideoDecoder : IPlatformVideoDecoder { }
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
