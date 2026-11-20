using System.Diagnostics;
using System.Globalization;

namespace BEngine.Platform.DotNet;

/// <summary>Independent, seekable stereo PCM cursor with bounded buffers and no full-track decoding.</summary>
public sealed class FfmpegMediaAudioReader : PlatformMediaAudioReader
{
    private readonly string _source, _executable;
    private readonly string? _temporary;
    private Process? _process;
    private Task<string>? _diagnostics;
    private byte[] _buffer = [];
    private int _position;
    private bool _ended;
    public override int Channels => 2;
    public override int Frequency => 48000;
    public override int Samples { get; }
    public override int Position => _position;

    public FfmpegMediaAudioReader(string source, ReadOnlyMemory<byte> bytes = default)
    {
        _executable = FfmpegMediaVideoReader.ResolveExecutable("ffmpeg") ?? throw new FileNotFoundException("Install FFmpeg or configure BENGINE_FFMPEG_PATH.");
        if (!bytes.IsEmpty)
        {
            _temporary = Path.Combine(Path.GetTempPath(), "bengine-video-audio-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(_temporary, bytes.ToArray()); source = _temporary;
        }
        _source = source;
        try
        {
            using var probe = new FfmpegMediaVideoReader(source);
            if (!probe.HasAudio) throw new InvalidDataException("The video has no audio track.");
            Samples = checked((int)Math.Ceiling(probe.Duration * Frequency));
        }
        catch { if (_temporary is not null) File.Delete(_temporary); throw; }
    }
    protected override int ReadCore(float[] output, int offset, int count)
    {
        var frames = Math.Min(count / Channels, Samples - _position);
        if (frames == 0) return 0;
        var bytes = checked(frames * Channels * sizeof(float));
        if (_buffer.Length < bytes) _buffer = new byte[bytes];
        try
        {
            var read = 0;
            if (!_ended)
            {
                if (_process is null)
                {
                    _process = FfmpegMediaVideoReader.Start(_executable, "-hide_banner", "-loglevel", "error", "-ss",
                        (_position / (double)Frequency).ToString("R", CultureInfo.InvariantCulture), "-i", _source,
                        "-map", "0:a:0", "-ac", "2", "-ar", "48000", "-f", "f32le", "pipe:1");
                    _diagnostics = _process.StandardError.ReadToEndAsync();
                }
                read = _process.StandardOutput.BaseStream.ReadAtLeastAsync(_buffer.AsMemory(0, bytes), bytes, throwOnEndOfStream: false)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
                if (read % (sizeof(float) * Channels) != 0) throw new InvalidDataException("FFmpeg returned incomplete PCM frames.");
                if (read < bytes)
                {
                    FfmpegMediaVideoReader.Finish(_process, TimeSpan.FromSeconds(30));
                    if (_process.ExitCode != 0) throw new InvalidDataException(_diagnostics!.GetAwaiter().GetResult());
                    StopProcess(); _ended = true;
                }
            }
            Buffer.BlockCopy(_buffer, 0, output, offset * sizeof(float), read);
            Array.Clear(output, offset + read / sizeof(float), frames * Channels - read / sizeof(float));
            _position += frames; return frames * Channels;
        }
        catch { StopProcess(); throw; }
    }
    protected override void SeekCore(int frame)
    {
        if (frame == _position) return;
        StopProcess(); _position = frame; _ended = false;
    }
    private void StopProcess()
    {
        if (_process is null) return;
        try { FfmpegMediaVideoReader.Kill(_process); _process.WaitForExit(5000); }
        finally { _process.Dispose(); _process = null; _diagnostics = null; }
    }
    protected override void DisposeCore()
    { StopProcess(); _buffer = []; if (_temporary is not null) File.Delete(_temporary); }
}
