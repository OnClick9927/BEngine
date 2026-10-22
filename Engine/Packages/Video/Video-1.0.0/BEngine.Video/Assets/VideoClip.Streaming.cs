using BEngine.Audio;

namespace BEngine.Video;

public sealed partial class VideoClip
{
    private string? _source;
    private byte[]? _mediaBytes;
    private double _nativeDuration, _nativeFrameRate;
    private BEngine.Platform.IPlatformVideoFrameReader? _asyncPrototype;
    private readonly HashSet<VideoFrameReader> _readers = [];
    public bool isNative { get; private set; }
    public bool isPortable => !isNative;
    internal bool requiresPolling => _asyncPrototype?.RequiresPolling == true;
    public VideoClipEncoding encoding => isNative ? VideoClipEncoding.OriginalContainer : VideoClipEncoding.PortableFrames;
    public long encodedMemoryBytes => isNative ? _mediaBytes?.LongLength ?? 0 : _data.LongLength;
    public int activeReaderCount { get { lock (_readers) return _readers.Count; } }

    public static VideoClip LoadNative(string pathOrUrl, bool includeAudio = true)
    { ArgumentException.ThrowIfNullOrWhiteSpace(pathOrUrl); return OpenNative(pathOrUrl, null, includeAudio); }
    /// <summary>Opens a native cursor asynchronously on the engine frame loop.</summary>
    public AsyncVideoFrameReader OpenAsyncReader(VideoDecodeMode mode = VideoDecodeMode.Automatic)
    {
        ThrowIfDestroyed();
        if (requiresPolling) throw new NotSupportedException("Host video cursors must be polled on the engine thread with OpenReader.");
        if (!isNative) throw new NotSupportedException("Portable frames can use OpenReader; asynchronous native cursors require a container clip.");
        var source = _source!; var bytes = _mediaBytes;
        return new(() => VideoDecoder.OpenVideo(source, bytes, mode));
    }

    private static VideoClip OpenNative(string source, byte[]? bytes, bool includeAudio)
    {
        using var probe = VideoDecoder.OpenVideo(source, bytes, VideoDecodeMode.Automatic);
        var clip = new VideoClip
        {
            name = "Video", isNative = true, _source = source, _mediaBytes = bytes,
            width = probe.width, height = probe.height, frameRate = (int)Math.Round(probe.frameRate),
            _nativeFrameRate = probe.frameRate, _nativeDuration = probe.duration
        };
        try
        {
            var url = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
            clip.name = System.IO.Path.GetFileNameWithoutExtension(url ? uri!.AbsolutePath : source);
            if (includeAudio && probe.hasAudio)
                clip.audioClip = AudioClip.CreateStream(clip.name + " Audio", () => VideoDecoder.OpenAudioContainer(source, bytes));
            if (!url) clip.BindAssetReference(source);
            return clip;
        }
        catch { DestroyImmediate(clip); throw; }
    }

    private static async Task<VideoClip> OpenNativeAsync(string source, ReadOnlyMemory<byte> bytes, bool includeAudio, CancellationToken cancellationToken)
    {
        var open = BEngine.Platform.PlatformVideoDecoding.OpenVideoAsync ?? throw new PlatformNotSupportedException("The host has no asynchronous container decoder.");
        var pending = open(source, bytes, false, cancellationToken);
        while (!pending.IsCompleted) await BTask.Yield();
        var probe = pending.GetAwaiter().GetResult();
        VideoClip? clip = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (probe.Width <= 0 || probe.Height <= 0 || (long)probe.Width * probe.Height > 16_777_216 ||
                !double.IsFinite(probe.Duration) || probe.Duration <= 0 || !double.IsFinite(probe.FrameRate) || probe.FrameRate is <= 0 or > 1000 || probe.Duration * probe.FrameRate > int.MaxValue)
                throw new InvalidDataException("Asynchronous video metadata exceeds decoder limits.");
            clip = new VideoClip { name = System.IO.Path.GetFileNameWithoutExtension(source), isNative = true, _source = source,
                _mediaBytes = bytes.IsEmpty ? null : bytes.ToArray(), _asyncPrototype = probe, width = probe.Width, height = probe.Height,
                frameRate = (int)Math.Round(probe.FrameRate), _nativeFrameRate = probe.FrameRate, _nativeDuration = probe.Duration };
            if (includeAudio && probe.HasAudio)
                clip.audioClip = AudioClip.CreateStream(clip.name + " Audio", () => probe.OpenAudio() is { } audio ? new PlatformVideoAudioReader(audio) : VideoDecoder.OpenAudioContainer(source, clip._mediaBytes));
            return clip;
        }
        catch { if (clip is not null) DestroyImmediate(clip); else probe.Dispose(); throw; }
    }

    public static Task<VideoClip> FromBytesAsync(ReadOnlyMemory<byte> bytes, string sourcePath = "Video", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length is < 1 or > 512 * 1024 * 1024) throw new ArgumentException("Video container must contain 1 byte to 512 MiB.", nameof(bytes));
        if (bytes.Length >= 8 && System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.Span) == Magic)
            return Task.FromResult(FromBytes(bytes, sourcePath));
        return BEngine.Platform.PlatformVideoDecoding.OpenVideoAsync is null ? Task.FromResult(OpenNative(sourcePath, bytes.ToArray(), true)) : OpenNativeAsync(sourcePath, bytes, true, cancellationToken);
    }

    /// <summary>Opens an independent bounded frame decoder. Destroying the clip closes all its readers.</summary>
    public VideoFrameReader OpenReader(VideoDecodeMode mode = VideoDecodeMode.Automatic)
    {
        ThrowIfDestroyed();
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!isNative && mode == VideoDecodeMode.HardwareRequired) throw new NotSupportedException("Portable frame clips use CPU decompression. Select Original Container import for native hardware decoding.");
        if (_asyncPrototype is not null && mode == VideoDecodeMode.HardwareRequired) throw new NotSupportedException("Browser video output does not prove hardware decoding.");
        VideoFrameReader raw = _asyncPrototype is not null ? VideoRuntimeSettings.Open(() => new PlatformVideoFrameReader(_asyncPrototype.Clone())) :
            isNative ? VideoDecoder.OpenVideo(_source!, _mediaBytes, mode) : VideoRuntimeSettings.Open(() => new PortableReader(this));
        if (raw.width != width || raw.height != height || Math.Abs(raw.frameRate - frameRateExact) > .0001 ||
            Math.Abs(raw.duration - (double)length) > .001)
        { raw.Dispose(); throw new InvalidDataException("The video source changed. Reload or reimport the clip."); }
        var reader = new OwnedReader(this, raw); lock (_readers) _readers.Add(reader); return reader;
    }

    private sealed class OwnedReader(VideoClip owner, VideoFrameReader raw) : VideoFrameReader
    {
        public override int width => raw.width;
        public override int height => raw.height;
        public override double frameRate => raw.frameRate;
        public override double duration => raw.duration;
        public override double timestamp => raw.timestamp;
        public override bool canSeek => raw.canSeek;
        public override string backend => raw.backend;
        public override bool hasAudio => raw.hasAudio;
        public override bool requiresPolling => raw.requiresPolling;
        public override bool hasPendingFrame => raw.hasPendingFrame;
        public override long hardwareDecodedFrames => raw.hardwareDecodedFrames;
        public override long decodedFrames => raw.decodedFrames;
        protected override void ReadFrameCore(double seconds, Span<byte> rgba) { owner.ThrowIfDestroyed(); raw.ReadFrame(seconds, rgba); }
        protected override void DisposeCore() { raw.Dispose(); lock (owner._readers) owner._readers.Remove(this); }
    }

    private sealed class PortableReader(VideoClip owner) : VideoFrameReader
    {
        private int _frame;
        private long _decodedFrames;
        public override int width => owner.width;
        public override int height => owner.height;
        public override double frameRate => owner.frameRate;
        public override double duration => (double)owner.length;
        public override double timestamp => _frame / frameRate;
        public override bool canSeek => true;
        public override string backend => "BEngine portable frames";
        public override long decodedFrames => _decodedFrames;
        protected override void ReadFrameCore(double seconds, Span<byte> rgba)
        {
            owner.ThrowIfDestroyed(); _frame = Math.Clamp((int)Math.Floor(seconds * frameRate + 1e-7), 0, owner.frameCount - 1);
            owner.GetPortableFrame(_frame, rgba);
            _decodedFrames++;
        }
    }
}
