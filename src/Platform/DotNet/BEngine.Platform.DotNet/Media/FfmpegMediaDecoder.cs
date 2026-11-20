namespace BEngine.Platform.DotNet;

/// <summary>Optional external container codec. Commands, pipes and process retirement stay with the host.</summary>
public sealed class FfmpegMediaDecoder : IPlatformExternalVideoDecoder
{
    public static FfmpegMediaDecoder Shared { get; } = new();
    public PlatformVideoDecoderCapabilities Capabilities => new(!OperatingSystem.IsBrowser() && FfmpegMediaVideoReader.IsAvailable, true, false);
    public IPlatformVideoFrameReader OpenVideo(string source, ReadOnlyMemory<byte> bytes, PlatformVideoOpenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Mode)) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.Mode == PlatformVideoDecodeMode.HardwareRequired) throw new NotSupportedException("The external frame reader uses CPU output and cannot prove hardware decode.");
        return new FfmpegMediaVideoReader(source, bytes, options.PrepareDimensions);
    }
    public IPlatformAudioSampleReader OpenAudio(string source, ReadOnlyMemory<byte> bytes) => new FfmpegMediaAudioReader(source, bytes);
}
