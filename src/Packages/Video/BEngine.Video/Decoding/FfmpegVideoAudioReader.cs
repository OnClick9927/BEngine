namespace BEngine.Video;

/// <summary>Explicit external-codec PCM cursor with Audio-domain validation and normalization.</summary>
public sealed class FfmpegVideoAudioReader : PlatformVideoAudioReader
{
    public FfmpegVideoAudioReader(string source, ReadOnlyMemory<byte> bytes = default)
        : base(VideoDecoder.ExternalDecoder.OpenAudio(source, bytes)) { }
}
