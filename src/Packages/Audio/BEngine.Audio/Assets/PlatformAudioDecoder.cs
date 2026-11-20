using BEngine.Platform;

namespace BEngine.Audio;

internal static class PlatformAudioDecoder
{
    internal static IPlatformAudioDecodeOperation? TryCreate(byte[]? encoded, int frames, int channels, int frequency, bool mono, float gain)
    {
        // These are clip-loading policy, not native API restrictions. Tiny effects
        // and large sources retain the bounded managed decoding path.
        if (!AudioLoadingSettings.usePlatformDecoding || encoded is not { Length: >= 32768 and <= 64 * 1024 * 1024 } ||
            frequency is < 8000 or > 96000) return null;
        return PlatformRuntime.GetService<IPlatformAudioDecoderFactory>()?.TryCreate(encoded,
            new PlatformAudioDecodeOptions(frames, channels, frequency, mono, gain));
    }
}
