namespace BEngine.Platform;

/// <summary>Requested PCM shape; loading thresholds, memory budgets and fallback policy belong to the consumer.</summary>
public readonly record struct PlatformAudioDecodeOptions(int Frames, int Channels, int Frequency, bool Mono, float Gain);

public enum PlatformAudioDecodeStatus { Pending, Ready, Failed }

/// <summary>One native asynchronous decode. Copy returns the number of interleaved samples written.</summary>
public interface IPlatformAudioDecodeOperation : IDisposable
{
    PlatformAudioDecodeStatus Status { get; }
    int Copy(int firstFrame, Span<float> destination);
}

public interface IPlatformAudioDecoderFactory
{
    /// <summary>The implementation must copy or own the encoded input before this call returns.</summary>
    IPlatformAudioDecodeOperation? TryCreate(ReadOnlyMemory<byte> encoded, PlatformAudioDecodeOptions options);
}
