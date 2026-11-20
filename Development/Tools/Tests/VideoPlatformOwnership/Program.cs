using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BEngine.Video;
using BEngine.Platform;

// A child-process protocol fixture exercises the real ffprobe/ffmpeg pipes without
// requiring an optional codec installation. Actual codec parity uses Windows MF below.
if (args.Contains("-show_entries"))
{
    var log = Environment.GetEnvironmentVariable("BENGINE_MEDIA_CALL_LOG");
    if (log is not null) File.AppendAllText(log, string.Join(" ", args) + "\n");
    Console.Write(args.Contains("frame=best_effort_timestamp_time")
        ? "0.000\n0.033,side data\n,empty first field\ninvalid\nNaN\nInfinity\n1e-1,side data\n 0.2 \n0.033,duplicate\n"
        : "{\"streams\":[{\"codec_type\":\"video\",\"width\":2,\"height\":1,\"avg_frame_rate\":\"30/1\"},{\"codec_type\":\"audio\"}],\"format\":{\"duration\":\"1.0\"}}");
    return;
}
if (args.Contains("pipe:1"))
{
    using var output = Console.OpenStandardOutput();
    if (args.Contains("rawvideo"))
    {
        var time = double.Parse(args[Array.IndexOf(args, "-ss") + 1], CultureInfo.InvariantCulture);
        Console.Error.WriteLine("pts_time:" + time.ToString("R", CultureInfo.InvariantCulture));
        output.Write(new byte[] { (byte)(time * 100), 20, 30, 255, 40, 50, 60, 255 });
    }
    else
    {
        using var binary = new BinaryWriter(output);
        for (var i = 0; i < 48000 * 2; i++) binary.Write((i % 4) switch { 0 => float.NaN, 1 => 2f, 2 => -2f, _ => .5f });
    }
    return;
}
var root = Path.GetFullPath(args[0]); var sample = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
var checks = new List<string>(); var payloads = new SortedDictionary<string, object>();
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
void Throws<T>(Action action, string name) where T : Exception
{ try { action(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
DesktopPlatformServices.Initialize();
var timer = Stopwatch.StartNew();
void Frames(string key, Func<VideoFrameReader> factory)
{
    using var reader = factory(); var buffer = new byte[reader.width * reader.height * 4];
    Check(reader.width > 0 && reader.height > 0 && reader.duration > 0 && reader.canSeek, key + ": metadata/seek");
    var records = new List<object>();
    foreach (var requested in new[] { 0, .07, .21, .49, 1, .12, .2 })
    {
        var time = Math.Min(requested, reader.duration); reader.ReadFrame(time, buffer);
        Check(reader.timestamp <= time + .05 && reader.timestamp >= 0, key + ": PTS " + requested);
        Check(buffer.Where((_, i) => i % 4 == 3).All(b => b == 255), key + ": opaque RGBA " + requested);
        records.Add(new { Time = requested, reader.timestamp, Hash = Convert.ToHexString(SHA256.HashData(buffer)), reader.decodedFrames, reader.hardwareDecodedFrames });
    }
    payloads[key] = new { reader.width, reader.height, reader.frameRate, reader.duration, reader.hasAudio, Records = records };
    Throws<ArgumentOutOfRangeException>(() => reader.ReadFrame(double.NaN, buffer), key + ": nonfinite rejected");
    Throws<ArgumentException>(() => reader.ReadFrame(0, new byte[1]), key + ": frame size rejected");
    reader.Dispose(); reader.Dispose();
    Throws<ObjectDisposedException>(() => reader.ReadFrame(0, buffer), key + ": disposed cursor rejected");
}
Frames("NativeFile", () => VideoDecoder.Open(sample, VideoDecodeMode.Software));
Frames("NativeMemory", () => VideoDecoder.Open(File.ReadAllBytes(sample), VideoDecodeMode.Software));
Check(VideoRuntimeSettings.activeDecoders == 0 && VideoRuntimeSettings.estimatedMemoryBytes == 0 && VideoDecoder.activeNativeSessions == 0, "Native readers release budget and sessions");
using (var listener = new MediaServer(File.ReadAllBytes(sample)))
{
    Frames("NativeHttp", () => VideoDecoder.Open(listener.Url, VideoDecodeMode.Software));
    Check(listener.Requests > 0, "System decoder reads actual HTTP fixture");
}
using (var reader = VideoDecoder.Open(sample))
{
    var pixels = new byte[reader.width * reader.height * 4]; reader.ReadFrame(0, pixels);
    Check(reader.decodedFrames > 0 && reader.hardwareDecodedFrames >= 0, "Automatic decoder reports actual counters");
    payloads["Automatic"] = new { reader.backend, reader.decodedFrames, reader.hardwareDecodedFrames };
}
using (var reader = VideoDecoder.Open(sample, VideoDecodeMode.Software))
{
    if (reader.hasAudio)
    {
        using var audio = VideoDecoder.OpenAudio(sample); var samples = new float[2048]; var records = new List<object>();
        foreach (var frame in new[] { 0, 9600, 4800, 0 })
        {
            audio.Seek(Math.Min(frame, audio.samples)); var read = audio.Read(samples, 0, samples.Length);
            Check(read > 0 && read % audio.channels == 0 && samples.All(float.IsFinite), "Native PCM seek/read " + frame);
            records.Add(new { Frame = frame, read, audio.position, Hash = Convert.ToHexString(SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples.AsSpan(0, read)))) });
        }
        payloads["NativeAudio"] = records;
    }
}
var oldBudget = VideoRuntimeSettings.maximumEstimatedMemoryBytes;
try
{
    VideoRuntimeSettings.maximumEstimatedMemoryBytes = 8 * 1024 * 1024 + 64;
    Throws<InvalidOperationException>(() => VideoDecoder.Open(sample, VideoDecodeMode.Software), "Native dimensions reject before allocating frame buffers");
    Check(VideoRuntimeSettings.activeDecoders == 0 && VideoDecoder.activeNativeSessions == 0, "Rejected native admission releases COM/session and budget");
}
finally { VideoRuntimeSettings.maximumEstimatedMemoryBytes = oldBudget; }
Throws<ArgumentException>(() => VideoDecoder.Open(Array.Empty<byte>()), "Empty container rejected");
Throws<ArgumentOutOfRangeException>(() => VideoDecoder.Open(sample, (VideoDecodeMode)999), "Unknown mode rejected");
Check(VideoRuntimeSettings.activeDecoders == 0 && VideoDecoder.activeNativeSessions == 0, "Failed opens do not leak");

var executable = Environment.ProcessPath!;
File.Copy(executable, Path.Combine(Path.GetDirectoryName(executable)!, "ffprobe.exe"), true);
Environment.SetEnvironmentVariable("BENGINE_FFMPEG_PATH", executable);
Check(FfmpegVideoFrameReader.IsAvailable, "External decoder resolves configured tool and sibling probe");
var culture = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
try
{
    using var external = new FfmpegVideoFrameReader("fixture.mp4", new byte[] { 1, 2, 3 });
    Check(external.indexedFrameCount == 4 && external.frameRate == 30 && external.duration == 1, "External metadata and CSV index are culture invariant");
    var pixels = new byte[8]; var records = new List<object>();
    foreach (var time in new[] { 0, 0, .04, .17, .21, 0 })
    { external.ReadFrame(time, pixels); records.Add(new { Time = time, external.timestamp, external.decodedFrames, Bytes = Convert.ToHexString(pixels) }); }
    Check(external.decodedFrames == 5, "External cached frame avoids a duplicate child process");
    payloads["ExternalFrames"] = records;
    using var audio = new FfmpegVideoAudioReader("fixture.mp4", new byte[] { 4, 5, 6 }); var pcm = new float[8];
    audio.Read(pcm, 0, pcm.Length);
    Check(pcm.SequenceEqual(new float[] { 0, 1, -1, .5f, 0, 1, -1, .5f }), "Domain normalizes external nonfinite and out-of-range PCM");
    audio.Seek(3); audio.Read(pcm, 0, pcm.Length);
    Check(audio.position == 7, "External seek restores independent audio cursor");
    payloads["ExternalAudio"] = new { Samples = pcm, audio.position, audio.frequency, audio.channels, audio.samples };
}
finally { CultureInfo.CurrentCulture = culture; }
Check(!Directory.GetFiles(Path.GetTempPath(), "bengine-video-*").Any(), "External memory-source temporary containers are retired");
#if !LEGACY
AdapterTests.Run(Check, root);
#endif
Check(VideoRuntimeSettings.activeDecoders == 0 && VideoDecoder.activeNativeSessions == 0, "Final decode/session budgets zero");
var result = new { Passed = true, Count = checks.Count, Checks = checks, Payloads = payloads, Milliseconds = timer.Elapsed.TotalMilliseconds,
    ExternalCodecValidation = "Real process/pipe protocol fixture; optional installed FFmpeg codec is not claimed. Actual codec regression uses Windows Media Foundation MP4." };
File.WriteAllText(Path.Combine(root, "Result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("VIDEO_OWNERSHIP_OK|" + checks.Count);

sealed class MediaServer : IDisposable
{
    private readonly HttpListener listener = new(); private readonly Task worker; private readonly byte[] content;
    public string Url { get; } public int Requests;
    public MediaServer(byte[] content)
    {
        this.content = content;
        var port = new TcpListener(IPAddress.Loopback, 0); port.Start(); var number = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
        Url = $"http://127.0.0.1:{number}/clip.mp4"; listener.Prefixes.Add($"http://127.0.0.1:{number}/"); listener.Start();
        worker = Task.Run(async () => { while (listener.IsListening) { HttpListenerContext context; try { context = await listener.GetContextAsync(); } catch when (!listener.IsListening) { break; } Serve(context); } });
    }
    private void Serve(HttpListenerContext context)
    {
        Interlocked.Increment(ref Requests);
        try
        {
            long start = 0, end = content.Length - 1; var range = context.Request.Headers["Range"];
            if (range?.StartsWith("bytes=") == true)
            {
                var parts = range[6..].Split('-'); start = long.Parse(parts[0]); if (parts[1].Length > 0) end = Math.Min(end, long.Parse(parts[1]));
                context.Response.StatusCode = 206; context.Response.Headers["Content-Range"] = $"bytes {start}-{end}/{content.Length}";
            }
            context.Response.ContentType = "video/mp4"; context.Response.Headers["Accept-Ranges"] = "bytes"; context.Response.ContentLength64 = end - start + 1;
            if (context.Request.HttpMethod != "HEAD") context.Response.OutputStream.Write(content, (int)start, (int)(end - start + 1));
        }
        catch (HttpListenerException) { }
        finally { context.Response.Close(); }
    }
    public void Dispose() { listener.Stop(); listener.Close(); worker.Wait(TimeSpan.FromSeconds(5)); }
}
