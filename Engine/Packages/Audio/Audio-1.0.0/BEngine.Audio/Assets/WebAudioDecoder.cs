using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace BEngine.Audio;

/// <summary>Owns one browser decode. No engine object or managed memory view is retained by JavaScript.</summary>
[SupportedOSPlatform("browser")]
internal sealed class WebAudioDecoder : IDisposable
{
    private int _handle;

    private WebAudioDecoder(int handle) => _handle = handle;

    internal static WebAudioDecoder? TryCreate(byte[]? encoded, int frames, int channels, int frequency, bool mono, float gain)
    {
        // Tiny effects are cheaper to decode directly; large/unsupported browser
        // sample rates retain the existing bounded managed path.
        if (!AudioLoadingSettings.usePlatformDecoding || encoded is not { Length: >= 32768 and <= 64 * 1024 * 1024 } ||
            frequency is < 8000 or > 96000) return null;
        try
        {
            var handle = WebAudioDecodeInterop.Begin(encoded.AsSpan(), frames, channels, frequency, mono, gain);
            return handle > 0 ? new(handle) : null;
        }
        catch (JSException) { return null; }
    }

    // 0 = decoding, 1 = decoded, -1 = unsupported/failed. A failed native
    // attempt is discarded once before opening the managed reader.
    internal int status => WebAudioDecodeInterop.Status(_handle);

    internal int Copy(int firstFrame, Span<float> destination) =>
        WebAudioDecodeInterop.Copy(_handle, firstFrame, MemoryMarshal.AsBytes(destination));

    public void Dispose()
    {
        if (_handle == 0) return;
        WebAudioDecodeInterop.Release(_handle);
        _handle = 0;
    }
}

[SupportedOSPlatform("browser")]
internal static partial class WebAudioDecodeInterop
{
    [JSImport("globalThis.BEngineWebAudio.beginDecode")]
    internal static partial int Begin([JSMarshalAs<JSType.MemoryView>] Span<byte> encoded,
        int frames, int channels, int frequency, bool mono, float gain);

    [JSImport("globalThis.BEngineWebAudio.decodeStatus")]
    internal static partial int Status(int handle);

    [JSImport("globalThis.BEngineWebAudio.copyDecodedPcm")]
    internal static partial int Copy(int handle, int firstFrame, [JSMarshalAs<JSType.MemoryView>] Span<byte> destination);

    [JSImport("globalThis.BEngineWebAudio.releaseDecode")]
    internal static partial void Release(int handle);
}
