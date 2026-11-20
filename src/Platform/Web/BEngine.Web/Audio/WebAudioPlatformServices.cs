using System.Runtime.Versioning;

namespace BEngine.Platform;

[SupportedOSPlatform("browser")]
internal sealed class WebAudioOutputFactory : IPlatformAudioOutputFactory
{
    internal static WebAudioOutputFactory Shared { get; } = new();
    public IPlatformAudioOutput CreateDefault(int sampleRate, int channelCount) => new WebAudioOutput(sampleRate, channelCount);
    public IPlatformAudioOutput CreateDefault(PlatformAudioOutputOptions options) =>
        new WebAudioOutput(options.SampleRate, options.ChannelCount, options.BufferMilliseconds);
}

[SupportedOSPlatform("browser")]
internal sealed class WebAudioDecoderFactory : IPlatformAudioDecoderFactory
{
    internal static WebAudioDecoderFactory Shared { get; } = new();
    public IPlatformAudioDecodeOperation? TryCreate(ReadOnlyMemory<byte> encoded, PlatformAudioDecodeOptions options) =>
        WebAudioDecoder.TryCreate(encoded, options.Frames, options.Channels, options.Frequency, options.Mono, options.Gain);
}
