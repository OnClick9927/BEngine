using BEngine.Audio;

namespace BEngine.Video;

/// <summary>Opens native media containers without transcoding. Each reader has an independent cursor.
/// Readers are owned by the caller and must be used/disposed on their creating engine thread.</summary>
public static class VideoDecoder
{
    public static VideoFrameReader Open(string pathOrUrl, VideoDecodeMode mode = VideoDecodeMode.Automatic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathOrUrl);
        return OpenVideo(pathOrUrl, null, mode);
    }
    public static VideoFrameReader Open(ReadOnlyMemory<byte> bytes, VideoDecodeMode mode = VideoDecodeMode.Automatic)
    {
        if (bytes.IsEmpty || bytes.Length > 512 * 1024 * 1024) throw new ArgumentException("Video container must contain 1 byte to 512 MiB.", nameof(bytes));
        return OpenVideo("Memory video", bytes.ToArray(), mode);
    }
    public static AudioSampleReader OpenAudio(string pathOrUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathOrUrl);
        return OpenAudioContainer(pathOrUrl, null);
    }
    internal static AudioSampleReader OpenAudioContainer(string source, byte[]? bytes) =>
        BEngine.Platform.PlatformVideoDecoding.OpenAudio is { } platform ? new PlatformVideoAudioReader(platform(source, bytes ?? ReadOnlyMemory<byte>.Empty)) :
        OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            ? new FfmpegVideoAudioReader(source, bytes ?? ReadOnlyMemory<byte>.Empty)
            : OperatingSystem.IsWindows() ? new WindowsVideoAudioReader(source, bytes)
            : throw new PlatformNotSupportedException("This platform does not supply native container audio. Import Portable Frames for mixer audio.");
    internal static VideoFrameReader OpenVideo(string source, byte[]? bytes, VideoDecodeMode mode)
        => VideoRuntimeSettings.Open(resize => OpenVideoCore(source, bytes, mode, resize));
    private static VideoFrameReader OpenVideoCore(string source, byte[]? bytes, VideoDecodeMode mode, Action<int, int> resize)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (BEngine.Platform.PlatformVideoDecoding.OpenVideo is { } platform)
            return new PlatformVideoFrameReader(platform(source, bytes ?? ReadOnlyMemory<byte>.Empty, mode == VideoDecodeMode.HardwareRequired));
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            if (mode == VideoDecodeMode.HardwareRequired) throw new NotSupportedException("The FFmpeg frame reader uses CPU output and cannot prove hardware decode.");
            return new FfmpegVideoFrameReader(source, bytes ?? ReadOnlyMemory<byte>.Empty);
        }
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("No container decoder was registered by this platform host. Import Portable Frames or register a native video decoder.");
        if (mode == VideoDecodeMode.Software) return new WindowsVideoFrameReader(source, bytes, false, false, resize);
        try { return new WindowsVideoFrameReader(source, bytes, true, mode == VideoDecodeMode.HardwareRequired, resize); }
        catch (Exception exception) when (mode == VideoDecodeMode.Automatic && exception is InvalidDataException or NotSupportedException)
        { return new WindowsVideoFrameReader(source, bytes, false, false, resize); }
    }
    public static int activeNativeSessions => WindowsMediaSession.ActiveSessions;
}

internal sealed class WindowsVideoFrameReader : VideoFrameReader
{
    private readonly WindowsMediaSession _session;
    private readonly bool _requireHardware;
    private byte[] _current, _next, _scratch = [];
    private long _currentTime, _nextTime;
    private bool _hasCurrent, _hasNext, _ended;
    private long _decoded, _hardware;
    public override int width => _session.Width;
    public override int height => _session.Height;
    public override double frameRate => _session.FramesPerSecond;
    public override double duration => _session.DurationSeconds;
    public override double timestamp => _currentTime / 10_000_000d;
    public override bool canSeek => _session.CanSeek;
    public override string backend => _hardware > 0 ? "Windows Media Foundation / D3D11" : "Windows Media Foundation / CPU";
    public override long hardwareDecodedFrames => _hardware;
    public override long decodedFrames => _decoded;
    public override bool hasAudio => _session.HasAudio;

    internal WindowsVideoFrameReader(string path, byte[]? bytes, bool hardware, bool requireHardware, Action<int, int>? prepareDimensions = null)
    {
        _session = new WindowsMediaSession(path, bytes, false, hardware); _requireHardware = requireHardware;
        try
        {
            prepareDimensions?.Invoke(width, height);
            _current = new byte[checked(width * height * 4)]; _next = new byte[_current.Length];
            _hasCurrent = Decode(_current, out _currentTime);
            if (!_hasCurrent) throw new InvalidDataException("The video has no decodable frames.");
        }
        catch { _session.Dispose(); throw; }
    }

    protected override void ReadFrameCore(double seconds, Span<byte> rgba)
    {
        var desired = checked((long)Math.Round(seconds * 10_000_000d));
        if (desired < _currentTime || (desired - _currentTime > 20_000_000 && canSeek))
        {
            _session.Seek(seconds); _ended = false; _hasNext = false;
            _hasCurrent = Decode(_current, out _currentTime);
            if (!_hasCurrent) throw new InvalidDataException("Seek returned no video frame.");
        }
        using var budget = LoopExecutionGuard.AllowIterations(20000);
        for (var i = 0; i < 20000; i++)
        {
            if (!_hasNext && !_ended) _hasNext = Decode(_next, out _nextTime);
            if (!_hasNext || _nextTime > desired) { _current.CopyTo(rgba); return; }
            if (_nextTime < _currentTime) throw new InvalidDataException("The decoder returned non-monotonic presentation timestamps.");
            (_current, _next) = (_next, _current); _currentTime = _nextTime; _hasNext = false;
        }
        throw new InvalidDataException("Video seek exceeded the per-request decode budget.");
    }

    private bool Decode(byte[] rgba, out long time)
    {
        if (!_session.Read(out var sample, out time)) { _ended = true; return false; }
        try
        {
            var data = WindowsMediaSession.ReadBuffer(sample, ref _scratch, out var hardware, out var packed);
            if (_requireHardware && !hardware) throw new NotSupportedException("This video/adapter did not produce a hardware decoder surface. Select Automatic or Software decoding.");
            var pitch = packed ? (_session.CodedWidth + 1) & ~1 : _session.DefaultStride;
            var uvStart = checked(pitch * _session.CodedHeight);
            if ((long)uvStart + pitch * ((_session.CodedHeight + 1) / 2) > data.Length) throw new InvalidDataException("The NV12 decoder returned a truncated frame.");
            var full = _session.NominalRange == 1; var bt709 = _session.YuvMatrix == 1;
            var kr = bt709 ? .2126 : .299; var kb = bt709 ? .0722 : .114; var kg = 1 - kr - kb;
            using var budget = LoopExecutionGuard.AllowIterations(checked(width * height + height + 1));
            for (var y = 0; y < _session.VisibleHeight; y++)
            for (var x = 0; x < _session.VisibleWidth; x++)
            {
                var sourceX = x + _session.CropX; var sourceY = y + _session.CropY;
                var luma = (data[sourceY * pitch + sourceX] - (full ? 0 : 16)) / (full ? 255d : 219d);
                var uv = uvStart + (sourceY / 2) * pitch + (sourceX & ~1);
                var cb = (data[uv] - 128) / (full ? 255d : 224d); var cr = (data[uv + 1] - 128) / (full ? 255d : 224d);
                var (targetX, targetY) = _session.RotationDegrees switch
                {
                    90 => (_session.VisibleHeight - 1 - y, x),
                    180 => (_session.VisibleWidth - 1 - x, _session.VisibleHeight - 1 - y),
                    270 => (y, _session.VisibleWidth - 1 - x),
                    _ => (x, y)
                };
                var index = (targetY * width + targetX) * 4;
                rgba[index] = Channel(luma + 2 * (1 - kr) * cr);
                rgba[index + 1] = Channel(luma - 2 * kb * (1 - kb) / kg * cb - 2 * kr * (1 - kr) / kg * cr);
                rgba[index + 2] = Channel(luma + 2 * (1 - kb) * cb); rgba[index + 3] = 255;
            }
            _decoded++; if (hardware) _hardware++; return true;
        }
        finally { WindowsMediaNative.Release(ref sample); }
    }
    private static byte Channel(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
    protected override void DisposeCore() { _session.Dispose(); _current = []; _next = []; _scratch = []; }
}

internal sealed class WindowsVideoAudioReader : AudioSampleReader
{
    private readonly WindowsMediaSession _session;
    private byte[] _scratch = [];
    private float[] _block = [], _nextBlock = [];
    private int _position, _blockStart, _blockFrames, _nextStart;
    private bool _ended, _hasNext;
    public override int channels => 2;
    public override int frequency => 48000;
    public override int samples { get; }
    public override int position => _position;
    internal WindowsVideoAudioReader(string path, byte[]? bytes)
    {
        _session = new WindowsMediaSession(path, bytes, true, false);
        try { samples = checked((int)Math.Ceiling(_session.DurationSeconds * frequency)); }
        catch { _session.Dispose(); throw; }
    }
    protected override int ReadCore(float[] output, int offset, int count)
    {
        var written = 0; var remaining = Math.Min(count / 2, samples - _position);
        using var budget = LoopExecutionGuard.AllowIterations(checked(remaining + 20000));
        var emptySamples = 0;
        while (remaining > 0)
        {
            if (_position >= _blockStart + _blockFrames && (!_ended || _hasNext))
            {
                if (++emptySamples > 20000) throw new InvalidDataException("Audio seek exceeded its decode budget.");
                LoadBlock();
                continue;
            }
            var silent = _ended && _position >= _blockStart + _blockFrames || _position < _blockStart;
            var frames = silent ? _position < _blockStart ? Math.Min(remaining, _blockStart - _position) : remaining
                : Math.Min(remaining, _blockStart + _blockFrames - _position);
            if (silent) Array.Clear(output, offset + written, frames * 2);
            else Array.Copy(_block, (_position - _blockStart) * 2, output, offset + written, frames * 2);
            written += frames * 2; remaining -= frames; _position += frames;
        }
        return written;
    }
    private void LoadBlock()
    {
        if (_hasNext) { (_block, _nextBlock) = (_nextBlock, _block); _blockStart = _nextStart; _hasNext = false; }
        else if (!ReadBlock(ref _block, out _blockStart)) { _blockFrames = 0; return; }
        _blockFrames = _block.Length / 2;
        // Media Foundation can clamp a preroll AAC packet to the same PTS as the
        // first audible packet. Look ahead before exposing PCM so the later packet
        // owns the overlap; no guessed codec delay is subtracted from the timeline.
        using var budget = LoopExecutionGuard.AllowIterations(4096);
        for (var i = 0; i < 4096 && !_ended; i++)
        {
            _hasNext = ReadBlock(ref _nextBlock, out _nextStart);
            if (!_hasNext) return;
            if (_nextStart > _blockStart)
            { _blockFrames = Math.Min(_blockFrames, _nextStart - _blockStart); return; }
            (_block, _nextBlock) = (_nextBlock, _block); _blockStart = _nextStart;
            _blockFrames = _block.Length / 2; _hasNext = false;
        }
        if (!_ended) throw new InvalidDataException("Audio decoder returned too many overlapping packets.");
    }
    private bool ReadBlock(ref float[] block, out int start)
    {
        start = 0;
        if (!_session.Read(out var sample, out var time)) { _ended = true; return false; }
        try
        {
            var bytes = WindowsMediaSession.ReadBuffer(sample, ref _scratch, out _, out _);
            if (bytes.Length % 8 != 0) throw new InvalidDataException("Decoded video audio is not interleaved stereo float PCM.");
            if (block.Length != bytes.Length / 4) block = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, block, 0, bytes.Length);
            start = checked((int)Math.Round(time * (frequency / 10_000_000d))); return true;
        }
        finally { WindowsMediaNative.Release(ref sample); }
    }
    protected override void SeekCore(int frame)
    {
        if (frame == _position) return;
        // Decode a short preroll after a random seek to rebuild the AAC filter bank.
        _session.Seek(Math.Max(0, frame / (double)frequency - .25));
        _position = frame; _blockStart = frame; _blockFrames = 0; _hasNext = false; _ended = false;
    }
    protected override void DisposeCore() { _session.Dispose(); _block = []; _nextBlock = []; _scratch = []; }
}
