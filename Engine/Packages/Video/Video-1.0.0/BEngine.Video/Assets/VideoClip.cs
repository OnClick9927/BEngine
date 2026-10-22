using System.IO.Compression;
using System.Runtime.CompilerServices;
using BEngine.Audio;
using BEngine.Platform;

namespace BEngine.Video;

/// <summary>A native media container or a portable frame artifact with an optional audio track.</summary>
[EditorIcon("Icons/Assets/AssetVideo.png")]
public sealed partial class VideoClip : BAsset
{
    private const ulong Magic = 0x3130303044495642; // BVID0001
    private byte[] _data = [];
    private (int Offset, int Length)[] _frames = [];
    public int width { get; private set; }
    public int height { get; private set; }
    public int frameRate { get; private set; }
    public int frameCount => isNative ? checked((int)Math.Ceiling(_nativeDuration * _nativeFrameRate - 1e-5)) : _frames.Length;
    public double frameRateExact => isNative ? _nativeFrameRate : frameRate;
    public Fix64 length => isNative ? (Fix64)_nativeDuration : frameRate <= 0 ? Fix64.Zero : (Fix64)frameCount / frameRate;
    public AudioClip? audioClip { get; private set; }

    private VideoClip() { }

    protected override object CaptureClonePayload() => new object?[] {
        width, height, frameRate, _data.Clone(), _frames.SelectMany(frame => new[] { frame.Offset, frame.Length }).ToArray(), audioClip,
        _source, _mediaBytes?.Clone(), _nativeDuration, _nativeFrameRate, isNative, _asyncPrototype };

    protected override void RestoreClonePayload(object? payload, Func<BObject, BObject> cloneObject)
    {
        if (payload is not object?[] data || data.Length is not (6 or 11 or 12)) throw new InvalidDataException("Invalid VideoClip clone payload.");
        width = (int)data[0]!; height = (int)data[1]!; frameRate = (int)data[2]!; _data = (byte[])data[3]!;
        var frames = (int[])data[4]!;
        _frames = new (int Offset, int Length)[frames.Length / 2];
        using var budget = LoopExecutionGuard.AllowIterations(_frames.Length);
        for (var index = 0; index < _frames.Length; index++) _frames[index] = (frames[index * 2], frames[index * 2 + 1]);
        audioClip = data.Length == 12 && data[11] is IPlatformVideoFrameReader ? null : data[5] is BObject audio ? (AudioClip)cloneObject(audio) : null;
        if (data.Length >= 11)
        { _source = (string?)data[6]; _mediaBytes = (byte[]?)data[7]; _nativeDuration = (double)data[8]!; _nativeFrameRate = (double)data[9]!; isNative = (bool)data[10]!; }
        if (data.Length == 12 && data[11] is IPlatformVideoFrameReader prototype) _asyncPrototype = prototype.Clone();
        if (_asyncPrototype is { HasAudio: true } native && data[5] is not null)
            audioClip = AudioClip.CreateStream(name + " Audio", () => new PlatformVideoAudioReader(native.OpenAudio() ?? throw new InvalidDataException("Cloned browser media has no audio track.")));
    }

    [ModuleInitializer]
    internal static void Register() => RuntimeAssetCodecRegistry.Register<VideoClip>(FromBytes);

    public static VideoClip Load(string path) => Load(path, includeAudio: true);

    public static VideoClip Load(string path, bool includeAudio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return OpenNative(path, null, includeAudio);
        using var input = PlatformRuntime.GetRequiredService<FileUtility>().OpenRead(path);
        Span<byte> header = stackalloc byte[8]; var read = input.ReadAtLeast(header, 8, false);
        if (read == 8 && System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(header) == Magic)
            return FromBytes(PlatformRuntime.GetRequiredService<FileUtility>().ReadAllBytes(path), path);
        return OpenNative(path, null, includeAudio);
    }

    public static async Task<VideoClip> LoadAsync(string path, bool includeAudio = true, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (PlatformVideoDecoding.OpenVideoAsync is not null)
        {
            var pending = LoadHostAsync(path, includeAudio, cancellationToken);
            while (!pending.IsCompleted) await BTask.Yield();
            return pending.GetAwaiter().GetResult();
        }
        await BTask.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return Load(path, includeAudio);
    }

    private static Task<VideoClip> LoadHostAsync(string path, bool includeAudio, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); cancellationToken.ThrowIfCancellationRequested();
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return OpenNativeAsync(path, ReadOnlyMemory<byte>.Empty, includeAudio, cancellationToken);
        var bytes = PlatformRuntime.GetRequiredService<FileUtility>().ReadAllBytes(path);
        if (bytes.Length >= 8 && System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes) == Magic) return Task.FromResult(FromBytes(bytes, path));
        return OpenNativeAsync(path, bytes, includeAudio, cancellationToken);
    }

    public static VideoClip FromBytes(ReadOnlyMemory<byte> bytes, string sourcePath = "Video")
    {
        if (bytes.Length > 512 * 1024 * 1024) throw new InvalidDataException("Video exceeds the 512 MiB artifact limit.");
        if (bytes.Length < 8 || System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.Span) != Magic)
            return OpenNative(sourcePath, bytes.ToArray(), true);
        var data = bytes.ToArray();
        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt64() != Magic) throw new InvalidDataException("Invalid BEngine video. Import the source video first.");
        var clip = new VideoClip();
        try
        {
            clip.width = reader.ReadInt32(); clip.height = reader.ReadInt32(); clip.frameRate = reader.ReadInt32();
            clip.name = PlatformRuntime.GetRequiredService<PathUtility>().GetFileNameWithoutExtension(sourcePath);
            var count = reader.ReadInt32();
            Validate(clip.width, clip.height, clip.frameRate, count);
            var samples = reader.ReadInt32();
            if (samples < 0 || samples % 2 != 0 || (long)samples * sizeof(float) > stream.Length - stream.Position)
                throw new InvalidDataException("Invalid video audio length.");
            if (samples > 0)
            {
                var pcm = new float[samples];
                using var budget = LoopExecutionGuard.AllowIterations(samples);
                for (var i = 0; i < samples; i++)
                {
                    pcm[i] = reader.ReadSingle();
                    if (!float.IsFinite(pcm[i])) throw new InvalidDataException("Invalid video audio sample.");
                }
                clip.audioClip = AudioClip.Create(clip.name + " Audio", samples / 2, 2, 48000);
                clip.audioClip.SetData(pcm, 0);
            }
            clip._data = data;
            clip._frames = new (int, int)[count];
            using (LoopExecutionGuard.AllowIterations(count))
            for (var i = 0; i < count; i++)
            {
                var size = reader.ReadInt32();
                if (size <= 0 || size > stream.Length - stream.Position)
                    throw new InvalidDataException("Truncated video frame.");
                clip._frames[i] = ((int)stream.Position, size);
                stream.Position += size;
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected data after video frames.");
            clip.BindAssetReference(sourcePath);
            return clip;
        }
        catch { DestroyImmediate(clip); throw; }
    }

    protected override void ReleaseAssetResources()
    {
        foreach (var reader in _readers.ToArray()) reader.Dispose();
        _asyncPrototype?.Dispose(); _asyncPrototype = null;
        if (audioClip is not null) Destroy(audioClip);
        audioClip = null;
        _data = [];
        _frames = [];
        _mediaBytes = null; _source = null;
        base.ReleaseAssetResources();
    }

    public void GetFrame(int frame, Span<byte> rgba)
    {
        ThrowIfDestroyed();
        if ((uint)frame >= (uint)frameCount) throw new ArgumentOutOfRangeException(nameof(frame));
        if (rgba.Length != checked(width * height * 4)) throw new ArgumentException("Frame buffer size does not match the clip.", nameof(rgba));
        if (isNative)
        {
            using var reader = OpenReader(); reader.ReadFrame(frame / frameRateExact, rgba); return;
        }
        GetPortableFrame(frame, rgba);
    }

    private void GetPortableFrame(int frame, Span<byte> rgba)
    {
        var entry = _frames[frame];
        using var input = new MemoryStream(_data, entry.Offset, entry.Length, writable: false);
        using var decoder = new DeflateStream(input, CompressionMode.Decompress);
        decoder.ReadExactly(rgba);
        if (decoder.ReadByte() != -1) throw new InvalidDataException("Decoded video frame exceeds its declared dimensions.");
    }

    /// <summary>Writes frames and interleaved stereo 48 kHz float PCM to a seekable artifact stream.</summary>
    public static void Write(Stream output, int width, int height, int frameRate, int frameCount,
        IEnumerable<ReadOnlyMemory<byte>> frames, ReadOnlySpan<float> audio = default)
    {
        Validate(width, height, frameRate, frameCount);
        if (!output.CanSeek) throw new ArgumentException("Video output must support seeking.", nameof(output));
        if (audio.Length % 2 != 0) throw new ArgumentException("Audio must contain interleaved stereo samples.", nameof(audio));
        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic); writer.Write(width); writer.Write(height); writer.Write(frameRate); writer.Write(frameCount);
        writer.Write(audio.Length);
        using (LoopExecutionGuard.AllowIterations(audio.Length))
        foreach (var sample in audio)
        {
            if (!float.IsFinite(sample)) throw new ArgumentException("Audio samples must be finite.", nameof(audio));
            writer.Write(Math.Clamp(sample, -1, 1));
        }
        var count = 0;
        using (LoopExecutionGuard.AllowIterations(frameCount))
        foreach (var frame in frames)
        {
            if (++count > frameCount || frame.Length != checked(width * height * 4))
                throw new ArgumentException("Video frame count or dimensions do not match.", nameof(frames));
            var header = output.Position;
            writer.Write(0);
            using (var encoder = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true)) encoder.Write(frame.Span);
            var end = output.Position;
            output.Position = header;
            writer.Write(checked((int)(end - header - sizeof(int))));
            output.Position = end;
            if (end > 512 * 1024 * 1024) throw new InvalidDataException("Video exceeds the 512 MiB artifact limit.");
        }
        if (count != frameCount) throw new ArgumentException("Video has fewer frames than declared.", nameof(frames));
    }

    private static void Validate(int width, int height, int fps, int count)
    {
        if (width is < 1 or > 2048 || height is < 1 or > 2048 || fps is < 1 or > 120 || count is < 1 or > 72000)
            throw new InvalidDataException("Video requires dimensions 1–2048, frame rate 1–120 and 1–72000 frames.");
    }
}
