using BEngine.Platform;

namespace BEngine.Video;

/// <summary>Explicit external-codec cursor; process I/O belongs to the host implementation.</summary>
public sealed class FfmpegVideoFrameReader : PlatformVideoFrameReader
{
    public static bool IsAvailable => PlatformRuntime.GetService<IPlatformExternalVideoDecoder>()?.Capabilities.Available == true;
    public int indexedFrameCount => Native.IndexedFrameCount;
    public FfmpegVideoFrameReader(string source, ReadOnlyMemory<byte> bytes = default)
        : base(VideoDecoder.ExternalDecoder.OpenVideo(source, bytes, new(PlatformVideoDecodeMode.Software))) { }
}
