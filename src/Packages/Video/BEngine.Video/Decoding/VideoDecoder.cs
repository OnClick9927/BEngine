using BEngine.Audio;
using BEngine.Platform;

namespace BEngine.Video;

/// <summary>Domain admission and adaptation of host-owned container cursors.</summary>
public static class VideoDecoder
{
    public static VideoFrameReader Open(string pathOrUrl, VideoDecodeMode mode = VideoDecodeMode.Automatic)
    { ArgumentException.ThrowIfNullOrWhiteSpace(pathOrUrl); return OpenVideo(pathOrUrl, null, mode); }
    public static VideoFrameReader Open(ReadOnlyMemory<byte> bytes, VideoDecodeMode mode = VideoDecodeMode.Automatic)
    {
        if (bytes.IsEmpty || bytes.Length > 512 * 1024 * 1024) throw new ArgumentException("Video container must contain 1 byte to 512 MiB.", nameof(bytes));
        return OpenVideo("Memory video", bytes.ToArray(), mode);
    }
    public static AudioSampleReader OpenAudio(string pathOrUrl)
    { ArgumentException.ThrowIfNullOrWhiteSpace(pathOrUrl); return OpenAudioContainer(pathOrUrl, null); }
    internal static AudioSampleReader OpenAudioContainer(string source, byte[]? bytes)
    {
        var data = bytes ?? ReadOnlyMemory<byte>.Empty;
        if (PlatformVideoDecoding.OpenAudio is { } legacy) return new PlatformVideoAudioReader(legacy(source, data));
        return new PlatformVideoAudioReader(Decoder.OpenAudio(source, data));
    }
    internal static VideoFrameReader OpenVideo(string source, byte[]? bytes, VideoDecodeMode mode)
        => VideoRuntimeSettings.Open(resize => OpenVideoCore(source, bytes, mode, resize));
    private static VideoFrameReader OpenVideoCore(string source, byte[]? bytes, VideoDecodeMode mode, Action<int, int> resize)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var data = bytes ?? ReadOnlyMemory<byte>.Empty;
        // Existing browser/mobile and custom delegate registrations keep precedence.
        if (PlatformVideoDecoding.OpenVideo is { } legacy)
            return new PlatformVideoFrameReader(legacy(source, data, mode == VideoDecodeMode.HardwareRequired));
        var options = new PlatformVideoOpenOptions(mode switch
        {
            VideoDecodeMode.Automatic => PlatformVideoDecodeMode.Automatic,
            VideoDecodeMode.Software => PlatformVideoDecodeMode.Software,
            VideoDecodeMode.HardwareRequired => PlatformVideoDecodeMode.HardwareRequired,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        }, resize);
        return new PlatformVideoFrameReader(Decoder.OpenVideo(source, data, options));
    }
    private static IPlatformVideoDecoder Decoder => PlatformRuntime.GetService<IPlatformVideoDecoder>() ??
        throw new PlatformNotSupportedException("No container decoder was registered by this platform host. Import Portable Frames or register a native video decoder.");
    internal static IPlatformExternalVideoDecoder ExternalDecoder => PlatformRuntime.GetService<IPlatformExternalVideoDecoder>() ??
        throw new PlatformNotSupportedException("The host has no external container decoder.");
    public static int activeNativeSessions => PlatformRuntime.GetService<IPlatformVideoDecoder>()?.ActiveSessions ?? 0;
}
