namespace BEngine.Audio;

[EditorIcon("Icons/Assets/AssetAudio.png")]
public sealed class AudioClip : BAsset
{
    private float[] _pcm = [];

    public int samples { get; private set; }
    public int channels { get; private set; }
    public int frequency { get; private set; }
    public Fix64 length => frequency <= 0 ? Fix64.Zero : (Fix64)samples / frequency;
    public AudioDataLoadState loadState { get; private set; } = AudioDataLoadState.Unloaded;
    public bool ambisonic => false;

    private AudioClip() { }

    public static AudioClip Create(string name, int lengthSamples, int channels, int frequency,
        bool stream = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (lengthSamples <= 0) throw new ArgumentOutOfRangeException(nameof(lengthSamples));
        if (channels is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(channels));
        if (frequency is < 8000 or > 384000) throw new ArgumentOutOfRangeException(nameof(frequency));
        if (stream) throw new NotSupportedException("Streaming AudioClip creation requires a custom audio decoder.");
        return new AudioClip
        {
            name = name,
            samples = lengthSamples,
            channels = channels,
            frequency = frequency,
            _pcm = new float[checked(lengthSamples * channels)],
            loadState = AudioDataLoadState.Loaded
        };
    }

    public static AudioClip Load(string path) => FromWav(File.ReadAllBytes(path), path);

    public static AudioClip Load(string path, bool forceToMono, bool normalize)
    {
        var clip = Load(path);
        if (forceToMono && clip.channels > 1) clip.ConvertToMono();
        if (normalize) clip.Normalize();
        return clip;
    }

    public bool GetData(float[] data, int offsetSamples)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (offsetSamples < 0 || offsetSamples > samples) return false;
        var offset = checked(offsetSamples * channels);
        if (data.Length > _pcm.Length - offset) return false;
        _pcm.AsSpan(offset, data.Length).CopyTo(data);
        return true;
    }

    public bool SetData(float[] data, int offsetSamples)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (offsetSamples < 0 || offsetSamples > samples) return false;
        var offset = checked(offsetSamples * channels);
        if (data.Length > _pcm.Length - offset) return false;
        for (var index = 0; index < data.Length; index++)
            _pcm[offset + index] = Math.Clamp(data[index], -1f, 1f);
        return true;
    }

    public bool LoadAudioData() => loadState == AudioDataLoadState.Loaded;
    public bool UnloadAudioData() => false;

    internal float Sample(int frame, int channel)
    {
        if ((uint)frame >= (uint)samples || channels <= 0) return 0;
        return _pcm[frame * channels + Math.Clamp(channel, 0, channels - 1)];
    }

    internal static AudioClip FromWav(ReadOnlyMemory<byte> bytes, string sourcePath)
    {
        var decoded = WavDecoder.Decode(bytes.Span);
        var clip = new AudioClip
        {
            name = Path.GetFileNameWithoutExtension(sourcePath),
            samples = decoded.FrameCount,
            channels = decoded.Channels,
            frequency = decoded.SampleRate,
            _pcm = decoded.Samples,
            loadState = AudioDataLoadState.Loaded
        };
        clip.BindAssetReference(sourcePath);
        return clip;
    }

    private void ConvertToMono()
    {
        var mono = new float[samples];
        for (var frame = 0; frame < samples; frame++)
        {
            double value = 0;
            for (var channel = 0; channel < channels; channel++) value += Sample(frame, channel);
            mono[frame] = (float)(value / channels);
        }
        channels = 1;
        _pcm = mono;
    }

    private void Normalize()
    {
        var maximum = _pcm.Select(Math.Abs).DefaultIfEmpty(0).Max();
        if (maximum <= float.Epsilon || maximum >= 1f) return;
        var scale = 1f / maximum;
        for (var index = 0; index < _pcm.Length; index++) _pcm[index] *= scale;
    }
}
