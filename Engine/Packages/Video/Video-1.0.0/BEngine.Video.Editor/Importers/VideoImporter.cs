using BEngine.Platform;
using System.Globalization;
using System.Text.Json;
using BEngine.Editor;

namespace BEngine.Video.Editor;

public enum VideoImportMode { OriginalContainer, PortableFrames }

public sealed class VideoImporter : AssetImporter
{
    private static FileUtility Files => EditorServices.GetRequiredService<FileUtility>();
    private static PathUtility Paths => EditorServices.GetRequiredService<PathUtility>();
    private static PlatformUtility Platform => EditorServices.GetRequiredService<PlatformUtility>();

    public VideoImportMode importMode { get; set; } = VideoImportMode.PortableFrames;
    public int maxWidth { get; set; } = 640;
    public int frameRate { get; set; } = 24;
    public bool importAudio { get; set; } = true;

    protected override void ReadSettings(IReadOnlyDictionary<string, string> settings)
    {
        base.ReadSettings(settings);
        importMode = settings.TryGetValue("importMode", out var mode) && Enum.TryParse<VideoImportMode>(mode, out var parsed) && Enum.IsDefined(parsed)
            ? parsed : VideoImportMode.PortableFrames;
        maxWidth = Get(settings, "maxWidth", 640);
        frameRate = Get(settings, "frameRate", 24);
        importAudio = Get(settings, "importAudio", true);
    }
    protected override void WriteSettings(IDictionary<string, string> settings)
    {
        base.WriteSettings(settings);
        settings["importMode"] = importMode.ToString();
        settings["maxWidth"] = maxWidth.ToString(CultureInfo.InvariantCulture);
        settings["frameRate"] = frameRate.ToString(CultureInfo.InvariantCulture);
        settings["importAudio"] = importAudio.ToString();
    }

    public override void Import(AssetImportContext context)
    {
        if (Paths.GetExtension(context.sourcePath).Equals(".bvideo", StringComparison.OrdinalIgnoreCase))
        {
            var clip = VideoClip.FromBytes(context.ReadSourceBytes());
            BObject.DestroyImmediate(clip);
            context.CopySourceToArtifact();
            return;
        }
        if (!Enum.IsDefined(importMode)) throw new InvalidDataException("Unknown video import mode.");
        if (importMode == VideoImportMode.OriginalContainer)
        {
            var clip = VideoClip.LoadNative(context.sourcePath);
            BObject.DestroyImmediate(clip);
            using var input = context.OpenSourceStream();
            using var artifact = context.OpenArtifactStream();
            var block = new byte[81920]; int read;
            while ((read = input.Read(block)) > 0) { context.cancellationToken.ThrowIfCancellationRequested(); artifact.Write(block, 0, read); }
            Files.Flush(artifact, flushToDisk: true); return;
        }
        using var output = context.OpenArtifactStream();
        Convert(context.sourcePath, output, Math.Clamp(maxWidth, 16, 2048), Math.Clamp(frameRate, 1, 120),
            importAudio, context.cancellationToken);
    }

    public static void Convert(string sourcePath, Stream output, int maxWidth = 640, int frameRate = 24,
        bool importAudio = true, CancellationToken cancellationToken = default)
    {
        if (maxWidth is < 16 or > 2048 || frameRate is < 1 or > 120) throw new ArgumentOutOfRangeException(nameof(maxWidth));
        string ffmpeg;
        try { ffmpeg = ResolveFFmpeg(); }
        catch (FileNotFoundException) when (Platform.IsWindows)
        {
            ConvertWithNativeDecoder(sourcePath, output, maxWidth, frameRate, importAudio, cancellationToken);
            return;
        }
        var probe = Paths.Combine(Paths.GetDirectoryName(ffmpeg)!, Platform.IsWindows ? "ffprobe.exe" : "ffprobe");
        if (!Files.FileExists(probe)) throw new FileNotFoundException($"{Paths.GetFileName(probe)} must be next to {Paths.GetFileName(ffmpeg)}.", probe);
        using var metadata = JsonDocument.Parse(Run(probe,
            ["-v", "error", "-show_streams", "-show_format", "-of", "json", sourcePath], cancellationToken));
        var streams = metadata.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(stream => stream.GetProperty("codec_type").GetString() == "video");
        if (video.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("Source has no video stream.");
        var sourceWidth = video.GetProperty("width").GetInt32();
        var sourceHeight = video.GetProperty("height").GetInt32();
        if (sourceWidth <= 0 || sourceHeight <= 0) throw new InvalidDataException("Invalid video dimensions.");
        var scale = Math.Min(1d, maxWidth / (double)Math.Max(sourceWidth, sourceHeight));
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        var durationText = metadata.RootElement.GetProperty("format").GetProperty("duration").GetString();
        if (!double.TryParse(durationText, CultureInfo.InvariantCulture, out var duration) || !double.IsFinite(duration) || duration <= 0)
            throw new InvalidDataException("Source video must report a finite positive duration.");
        if (duration * frameRate > 72000 || duration * frameRate * width * height * 4 > 4L * 1024 * 1024 * 1024)
            throw new InvalidDataException("Video is too large to import. Trim the clip or reduce its resolution/frame rate in Import Settings.");
        var temporary = Paths.Combine(Platform.TemporaryDirectory, "BEngine.Video." + Guid.NewGuid().ToString("N"));
        Files.CreateDirectory(temporary);
        try
        {
            var raw = Paths.Combine(temporary, "frames.rgba");
            Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", sourcePath, "-map", "0:v:0", "-an",
                "-vf", $"scale={width}:{height},fps={frameRate}", "-pix_fmt", "rgba", "-f", "rawvideo", raw], cancellationToken);
            var frameBytes = checked(width * height * 4);
            var fileLength = Files.GetFileLength(raw);
            if (fileLength % frameBytes != 0) throw new InvalidDataException("Decoder produced an incomplete video frame.");
            var count = checked((int)(fileLength / frameBytes));
            float[] pcm = [];
            if (importAudio && streams.Any(stream => stream.GetProperty("codec_type").GetString() == "audio"))
            {
                var audioPath = Paths.Combine(temporary, "audio.pcm");
                Run(ffmpeg, ["-nostdin", "-v", "error", "-y", "-i", sourcePath, "-map", "0:a:0", "-vn",
                    "-ac", "2", "-ar", "48000", "-t", (count / (double)frameRate).ToString("R", CultureInfo.InvariantCulture),
                    "-f", "f32le", audioPath], cancellationToken);
                var audio = Files.ReadAllBytes(audioPath);
                if (audio.Length % 8 != 0) throw new InvalidDataException("Decoder produced incomplete stereo samples.");
                pcm = new float[audio.Length / 4];
                Buffer.BlockCopy(audio, 0, pcm, 0, audio.Length);
            }
            VideoClip.Write(output, width, height, frameRate, count, Frames(raw, frameBytes, cancellationToken), pcm);
        }
        finally { Files.DeleteDirectory(temporary, recursive: true); }
    }

    private static IEnumerable<ReadOnlyMemory<byte>> Frames(string path, int size, CancellationToken cancellationToken)
    {
        using var input = Files.OpenRead(path);
        var frame = new byte[size];
        while (input.Position < input.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            input.ReadExactly(frame);
            yield return frame;
        }
    }

    private static void ConvertWithNativeDecoder(string sourcePath, Stream output, int maxWidth, int frameRate,
        bool importAudio, CancellationToken cancellationToken)
    {
        var clip = VideoClip.LoadNative(sourcePath, importAudio);
        try
        {
            var scale = Math.Min(1d, maxWidth / (double)Math.Max(clip.width, clip.height));
            var width = Math.Max(1, (int)Math.Round(clip.width * scale));
            var height = Math.Max(1, (int)Math.Round(clip.height * scale));
            var frameCount = Math.Max(1, checked((int)Math.Ceiling((double)clip.length * frameRate - 1e-7)));
            if (frameCount > 72_000 || (long)frameCount * width * height * 4 > 4L * 1024 * 1024 * 1024)
                throw new InvalidDataException("Video is too large to import. Trim the clip or reduce its resolution/frame rate in Import Settings.");
            float[] pcm = [];
            if (importAudio && clip.audioClip is { } audio)
            {
                var samples = Math.Min(audio.samples, checked((int)Math.Ceiling(frameCount / (double)frameRate * 48_000)));
                pcm = new float[checked(samples * 2)];
                if (!audio.GetData(pcm, 0)) throw new InvalidDataException("The native decoder could not read the video audio track.");
            }
            VideoClip.Write(output, width, height, frameRate, frameCount,
                NativeFrames(clip, width, height, frameRate, frameCount, cancellationToken), pcm);
        }
        finally { BObject.DestroyImmediate(clip); }
    }

    private static IEnumerable<ReadOnlyMemory<byte>> NativeFrames(VideoClip clip, int width, int height,
        int frameRate, int frameCount, CancellationToken cancellationToken)
    {
        using var reader = clip.OpenReader(VideoDecodeMode.Software);
        var source = new byte[checked(clip.width * clip.height * 4)];
        var target = new byte[checked(width * height * 4)];
        for (var frame = 0; frame < frameCount; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reader.ReadFrame(frame / (double)frameRate, source);
            if (width == clip.width && height == clip.height) source.CopyTo(target, 0);
            else ResizeRgba(source, clip.width, clip.height, target, width, height);
            yield return target;
        }
    }

    private static void ResizeRgba(ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight,
        Span<byte> target, int width, int height)
    {
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sourceX = Math.Min(sourceWidth - 1, x * sourceWidth / width);
            var sourceY = Math.Min(sourceHeight - 1, y * sourceHeight / height);
            source.Slice((sourceY * sourceWidth + sourceX) * 4, 4).CopyTo(target.Slice((y * width + x) * 4, 4));
        }
    }

    public static string ResolveFFmpeg()
    {
        var configured = Platform.GetEnvironmentVariable("BENGINE_FFMPEG_PATH");
        if (string.IsNullOrWhiteSpace(configured)) configured = EditorPrefs.GetString("BEngine.Video.FFmpegPath");
        if (!string.IsNullOrWhiteSpace(configured) && Files.FileExists(configured)) return Paths.GetFullPath(configured);
        foreach (var directory in (Platform.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Paths.PathSeparator))
        {
            var candidate = Paths.Combine(directory.Trim('"'), Platform.IsWindows ? "ffmpeg.exe" : "ffmpeg");
            if (Files.FileExists(candidate)) return Paths.GetFullPath(candidate);
        }
        throw new FileNotFoundException("Video import needs FFmpeg and ffprobe. Use Tools/Video/Configure FFmpeg, or set BENGINE_FFMPEG_PATH to ffmpeg.exe. Existing .bvideo clips play without FFmpeg.");
    }

    private static string Run(string executable, string[] arguments, CancellationToken cancellationToken)
    {
        var request = new ProcessRunRequest(executable);
        foreach (var argument in arguments) request.Arguments.Add(argument);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var result = EditorServices.GetRequiredService<ProcessUtility>().Run(request, timeout.Token);
        if (result.ExitCode != 0)
            throw new InvalidDataException($"Video decoder failed ({result.ExitCode}): {result.StandardError}");
        return result.StandardOutput;
    }
}
