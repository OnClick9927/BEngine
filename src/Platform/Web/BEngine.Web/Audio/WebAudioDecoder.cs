using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace BEngine.Platform;

/// <summary>Owns one browser decode. No engine object or managed memory view is retained by JavaScript.</summary>
[SupportedOSPlatform("browser")]
internal sealed class WebAudioDecoder : IPlatformAudioDecodeOperation
{
    private int _handle;

    private WebAudioDecoder(int handle) => _handle = handle;

    internal static WebAudioDecoder? TryCreate(ReadOnlyMemory<byte> encoded, int frames, int channels, int frequency, bool mono, float gain)
    {
        try
        {
            var handle = WebAudioDecodeInterop.Begin(MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(encoded.Span), encoded.Length), frames, channels, frequency, mono, gain);
            return handle > 0 ? new(handle) : null;
        }
        catch (JSException) { return null; }
    }

    // 0 = decoding, 1 = decoded, -1 = unsupported/failed. A failed native
    // attempt is discarded once before opening the managed reader.
    public PlatformAudioDecodeStatus Status
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle == 0, this);
            return WebAudioDecodeInterop.Status(_handle) switch { 0 => PlatformAudioDecodeStatus.Pending, 1 => PlatformAudioDecodeStatus.Ready, _ => PlatformAudioDecodeStatus.Failed };
        }
    }

    public int Copy(int firstFrame, Span<float> destination)
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        return WebAudioDecodeInterop.Copy(_handle, firstFrame, MemoryMarshal.AsBytes(destination));
    }

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
