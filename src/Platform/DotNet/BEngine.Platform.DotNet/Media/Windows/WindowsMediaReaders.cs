using BEngine.Platform.DotNet;

namespace BEngine.Platform;

// Shared native cursors also serve command-line import and build sessions.

internal sealed class WindowsVideoFrameReader : PlatformVideoReader
{
    private readonly WindowsMediaSession _session;
    private readonly bool _requireHardware;
    private byte[] _current, _next, _scratch = [];
    private long _currentTime, _nextTime;
    private bool _hasCurrent, _hasNext, _ended;
    private long _decoded, _hardware;
    public override int Width => _session.Width;
    public override int Height => _session.Height;
    public override double FrameRate => _session.FramesPerSecond;
    public override double Duration => _session.DurationSeconds;
    public override double Timestamp => _currentTime / 10_000_000d;
    public override bool CanSeek => _session.CanSeek;
    public override string Backend => _hardware > 0 ? "Windows Media Foundation / D3D11" : "Windows Media Foundation / CPU";
    public override long HardwareDecodedFrames => _hardware;
    public override long DecodedFrames => _decoded;
    public override bool HasAudio => _session.HasAudio;

    internal WindowsVideoFrameReader(string path, byte[]? bytes, bool hardware, bool requireHardware, Action<int, int>? prepareDimensions = null)
    {
        _session = new WindowsMediaSession(path, bytes, false, hardware); _requireHardware = requireHardware;
        try
        {
            prepareDimensions?.Invoke(Width, Height);
            _current = new byte[checked(Width * Height * 4)]; _next = new byte[_current.Length];
            _hasCurrent = Decode(_current, out _currentTime);
            if (!_hasCurrent) throw new InvalidDataException("The video has no decodable frames.");
        }
        catch { _session.Dispose(); throw; }
    }

    protected override void ReadFrameCore(double seconds, Span<byte> rgba)
    {
        var desired = checked((long)Math.Round(seconds * 10_000_000d));
        if (desired < _currentTime || (desired - _currentTime > 20_000_000 && CanSeek))
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
            using var budget = LoopExecutionGuard.AllowIterations(checked(Width * Height + Height + 1));
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
                var index = (targetY * Width + targetX) * 4;
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

internal sealed class WindowsVideoAudioReader : PlatformMediaAudioReader
{
    private readonly WindowsMediaSession _session;
    private byte[] _scratch = [];
    private float[] _block = [], _nextBlock = [];
    private int _position, _blockStart, _blockFrames, _nextStart;
    private bool _ended, _hasNext;
    public override int Channels => 2;
    public override int Frequency => 48000;
    public override int Samples { get; }
    public override int Position => _position;
    internal WindowsVideoAudioReader(string path, byte[]? bytes)
    {
        _session = new WindowsMediaSession(path, bytes, true, false);
        try { Samples = checked((int)Math.Ceiling(_session.DurationSeconds * Frequency)); }
        catch { _session.Dispose(); throw; }
    }
    protected override int ReadCore(float[] output, int offset, int count)
    {
        var written = 0; var remaining = Math.Min(count / 2, Samples - _position);
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
            start = checked((int)Math.Round(time * (Frequency / 10_000_000d))); return true;
        }
        finally { WindowsMediaNative.Release(ref sample); }
    }
    protected override void SeekCore(int frame)
    {
        if (frame == _position) return;
        // Decode a short preroll after a random seek to rebuild the AAC filter bank.
        _session.Seek(Math.Max(0, frame / (double)Frequency - .25));
        _position = frame; _blockStart = frame; _blockFrames = 0; _hasNext = false; _ended = false;
    }
    protected override void DisposeCore() { _session.Dispose(); _block = []; _nextBlock = []; _scratch = []; }
}
