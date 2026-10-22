using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace BEngine.Video;

/// <summary>Bounded desktop container reader. Linux/macOS use installed FFmpeg; pixels are decoded on demand.</summary>
public sealed class FfmpegVideoFrameReader : VideoFrameReader
{
    private readonly string _source, _ffmpeg;
    private readonly string? _temporary;
    private readonly byte[] _pixels;
    private readonly double[] _frameTimes;
    private double _timestamp;
    private long _frames;
    private int _cachedIndex = -1;
    public override int width { get; }
    public override int height { get; }
    public override double frameRate { get; }
    public override double duration { get; }
    public override double timestamp => _timestamp;
    public override bool canSeek => true;
    public override string backend => "FFmpeg / CPU";
    public override bool hasAudio { get; }
    public override long decodedFrames => _frames;
    /// <summary>Actual presentation timestamps used for frame-accurate variable-rate seeking.</summary>
    public int indexedFrameCount => _frameTimes.Length;
    public static bool IsAvailable => ResolveExecutable("ffmpeg") is not null && ResolveExecutable("ffprobe") is not null;
    public FfmpegVideoFrameReader(string source, ReadOnlyMemory<byte> bytes = default)
    {
        _ffmpeg = ResolveExecutable("ffmpeg") ?? throw new FileNotFoundException("Install FFmpeg or configure BENGINE_FFMPEG_PATH.");
        var probe = ResolveExecutable("ffprobe") ?? throw new FileNotFoundException("ffprobe must be next to ffmpeg, or available on PATH.");
        if (!bytes.IsEmpty) { _temporary = Path.Combine(Path.GetTempPath(), "bengine-video-" + Guid.NewGuid().ToString("N")); File.WriteAllBytes(_temporary, bytes.ToArray()); source = _temporary; }
        _source = source;
        try
        {
            using var process = Start(probe, "-v", "error", "-show_entries", "stream=codec_type,width,height,avg_frame_rate,duration:stream_side_data=rotation:format=duration", "-of", "json", source);
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            Finish(process, TimeSpan.FromSeconds(30));
            if (process.ExitCode != 0) throw new InvalidDataException(error.GetAwaiter().GetResult());
            using var json = JsonDocument.Parse(output.GetAwaiter().GetResult());
            var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            var video = streams.First(stream => stream.GetProperty("codec_type").GetString() == "video");
            width = video.GetProperty("width").GetInt32(); height = video.GetProperty("height").GetInt32();
            if (video.TryGetProperty("side_data_list", out var sideData))
                foreach (var item in sideData.EnumerateArray())
                    if (item.TryGetProperty("rotation", out var rotation) && Math.Abs(rotation.GetDouble() % 180) == 90)
                    { (width, height) = (height, width); break; }
            var rate = video.GetProperty("avg_frame_rate").GetString()!.Split('/');
            frameRate = double.Parse(rate[0], CultureInfo.InvariantCulture) / double.Parse(rate[1], CultureInfo.InvariantCulture);
            duration = double.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
            hasAudio = streams.Any(stream => stream.GetProperty("codec_type").GetString() == "audio");
            if (width <= 0 || height <= 0 || (long)width * height > 16_777_216 || !double.IsFinite(frameRate) || frameRate <= 0 || !double.IsFinite(duration) || duration <= 0)
                throw new InvalidDataException("FFmpeg video metadata is invalid or exceeds decoder limits.");
            _pixels = new byte[checked(width * height * 4)];
            using var indexProcess = Start(probe, "-v", "error", "-select_streams", "v:0", "-show_entries", "frame=best_effort_timestamp_time", "-of", "csv=p=0", source);
            var indexError = indexProcess.StandardError.ReadToEndAsync();
            var indexTask = ReadIndexAsync();
            async Task<double[]> ReadIndexAsync()
            {
                var times = new List<double>();
                while (await indexProcess.StandardOutput.ReadLineAsync() is { } line)
                {
                    var field = line.Split(',')[0];
                    if (!double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var time) || !double.IsFinite(time)) continue;
                    if (times.Count == 1_000_000) { Kill(indexProcess); throw new InvalidDataException("Video frame index exceeds 1000000 entries."); }
                    times.Add(time);
                }
                return times.Distinct().Order().ToArray();
            }
            Finish(indexProcess, TimeSpan.FromSeconds(30));
            if (indexProcess.ExitCode != 0) throw new InvalidDataException(indexError.GetAwaiter().GetResult());
            _frameTimes = indexTask.GetAwaiter().GetResult();
            if (_frameTimes.Length == 0) throw new InvalidDataException("Video has no indexed presentation timestamps.");
        }
        catch { if (_temporary is not null) File.Delete(_temporary); throw; }
    }
    protected override void ReadFrameCore(double seconds, Span<byte> rgba)
    {
        var index = Array.BinarySearch(_frameTimes, seconds + 1e-7);
        if (index < 0) index = ~index - 1;
        index = Math.Clamp(index, 0, _frameTimes.Length - 1);
        if (_cachedIndex == index) { _pixels.CopyTo(rgba); return; }
        var requested = _frameTimes[index];
        using var process = Start(_ffmpeg, "-hide_banner", "-loglevel", "info", "-ss", requested.ToString("R", CultureInfo.InvariantCulture), "-copyts", "-i", _source,
            "-map", "0:v:0", "-frames:v", "1", "-vf", "showinfo", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1");
        var error = process.StandardError.ReadToEndAsync();
        try { process.StandardOutput.BaseStream.ReadExactlyAsync(_pixels).AsTask().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult(); Finish(process, TimeSpan.FromSeconds(30)); }
        catch { Kill(process); throw; }
        var diagnostics = error.GetAwaiter().GetResult(); if (process.ExitCode != 0) throw new InvalidDataException(diagnostics);
        var marker = diagnostics.IndexOf("pts_time:", StringComparison.Ordinal); _timestamp = requested;
        if (marker >= 0)
        {
            var start = marker + 9; var end = start;
            while (end < diagnostics.Length && !char.IsWhiteSpace(diagnostics[end])) end++;
            if (double.TryParse(diagnostics[start..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var pts)) _timestamp = pts;
        }
        _pixels.CopyTo(rgba); _cachedIndex = index; _frames++;
    }
    protected override void DisposeCore() { if (_temporary is not null) File.Delete(_temporary); }
    internal static string? ResolveExecutable(string name)
    {
        var suffix = OperatingSystem.IsWindows() ? ".exe" : "";
        var configured = Environment.GetEnvironmentVariable("BENGINE_FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        { var candidate = name == "ffmpeg" ? configured : Path.Combine(Path.GetDirectoryName(configured)!, name + suffix); if (File.Exists(candidate)) return candidate; }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        { var candidate = Path.Combine(directory.Trim('"'), name + suffix); if (File.Exists(candidate)) return candidate; }
        return null;
    }
    internal static Process Start(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start " + executable);
    }
    internal static void Finish(Process process, TimeSpan timeout)
    { try { process.WaitForExitAsync().WaitAsync(timeout).GetAwaiter().GetResult(); } catch { Kill(process); throw; } }
    internal static void Kill(Process process) { if (!process.HasExited) process.Kill(entireProcessTree: true); }
}
