namespace BEngine.Audio;

[AddComponentMenu("Audio/Audio Source")]
public sealed class AudioSource : MonoBehaviour
{
    [NonSerialized] private Fix64 _sampleCursor;
    [NonSerialized] private bool _playing;
    [NonSerialized] private bool _paused;
    [NonSerialized] private Fix64 _delay;
    [NonSerialized] private readonly List<OneShotVoice> _oneShots = [];
    private AudioClip? _clip;

    public AudioClip? clip
    {
        get => _clip;
        set
        {
            if (ReferenceEquals(_clip, value)) return;
            _clip = value;
            Stop();
        }
    }
    [Range(0, 1)] public Fix64 volume { get; set; } = Fix64.One;
    [Range(-1, 1)] public Fix64 panStereo { get; set; }
    [Range(0.01f, 3)] public Fix64 pitch { get; set; } = Fix64.One;
    public bool loop { get; set; }
    public bool playOnAwake { get; set; } = true;
    public bool mute { get; set; }
    [Range(0, 256)] public int priority { get; set; } = 128;
    public bool isPlaying => _playing && !_paused;
    public int timeSamples
    {
        get => Mathf.FloorToInt(_sampleCursor);
        set => _sampleCursor = Fix64.Clamp(value, 0, Math.Max(0, clip?.samples - 1 ?? 0));
    }
    public Fix64 time
    {
        get => clip is { frequency: > 0 } value ? _sampleCursor / value.frequency : Fix64.Zero;
        set
        {
            if (clip is not { } current) return;
            timeSamples = Mathf.FloorToInt(Fix64.Max(Fix64.Zero, value) * current.frequency);
        }
    }

    public override void Start()
    {
        if (playOnAwake && clip is not null) Play();
    }

    public override void OnDisable() => Stop();

    public void Play()
    {
        if (clip is null) return;
        _sampleCursor = Fix64.Zero;
        _delay = Fix64.Zero;
        _paused = false;
        _playing = true;
    }

    public void PlayDelayed(Fix64 delay)
    {
        if (delay < Fix64.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        Play();
        _delay = delay;
    }

    public void PlayOneShot(AudioClip oneShotClip) => PlayOneShot(oneShotClip, Fix64.One);

    public void PlayOneShot(AudioClip oneShotClip, Fix64 volumeScale)
    {
        ArgumentNullException.ThrowIfNull(oneShotClip);
        _oneShots.Add(new OneShotVoice(oneShotClip,
            Fix64.Clamp(volumeScale, Fix64.Zero, Fix64.One)));
    }

    public void Pause() => _paused = _playing;
    public void UnPause() => _paused = false;

    public void Stop()
    {
        _playing = false;
        _paused = false;
        _delay = Fix64.Zero;
        _sampleCursor = Fix64.Zero;
        _oneShots.Clear();
    }

    internal void Mix(Span<float> output, int outputSampleRate, int outputChannels, Fix64 deltaTime)
    {
        if (mute || AudioListener.pause || !enabled || !gameObject.activeInHierarchy) return;
        var gain = Fix64.Clamp(volume, Fix64.Zero, Fix64.One) * AudioListener.volume;
        var pan = Fix64.Clamp(panStereo, -Fix64.One, Fix64.One);
        if (_playing && !_paused && clip is { } current)
        {
            if (_delay > Fix64.Zero)
            {
                _delay -= deltaTime;
                if (_delay > Fix64.Zero) goto OneShots;
            }
            MixVoice(output, outputSampleRate, outputChannels, current, gain, pan,
                ref _sampleCursor, loop, ref _playing);
        }

        OneShots:
        for (var index = _oneShots.Count - 1; index >= 0; index--)
        {
            var voice = _oneShots[index];
            var playing = true;
            var cursor = voice.Cursor;
            MixVoice(output, outputSampleRate, outputChannels, voice.Clip, gain * voice.Volume, pan,
                ref cursor, false, ref playing);
            voice.Cursor = cursor;
            if (!playing) _oneShots.RemoveAt(index);
            else _oneShots[index] = voice;
        }
    }

    private void MixVoice(Span<float> output, int outputSampleRate, int outputChannels,
        AudioClip source, Fix64 gain, Fix64 pan, ref Fix64 cursor, bool repeat, ref bool playing)
    {
        var frames = output.Length / outputChannels;
        var step = (Fix64)source.frequency / outputSampleRate * Fix64.Clamp(pitch, Fix64.Parse("0.01"), 3);
        var leftGain = gain * (pan > Fix64.Zero ? Fix64.One - pan : Fix64.One);
        var rightGain = gain * (pan < Fix64.Zero ? Fix64.One + pan : Fix64.One);
        for (var frame = 0; frame < frames; frame++)
        {
            var sourceFrame = Mathf.FloorToInt(cursor);
            if (sourceFrame >= source.samples)
            {
                if (!repeat)
                {
                    playing = false;
                    break;
                }
                cursor %= source.samples;
                sourceFrame = Mathf.FloorToInt(cursor);
            }
            var left = source.Sample(sourceFrame, 0);
            var right = source.channels > 1 ? source.Sample(sourceFrame, 1) : left;
            if (outputChannels == 1)
                output[frame] += (float)(((Fix64)left * leftGain + (Fix64)right * rightGain) * Fix64.Half);
            else
            {
                output[frame * outputChannels] += (float)((Fix64)left * leftGain);
                output[frame * outputChannels + 1] += (float)((Fix64)right * rightGain);
            }
            cursor += step;
        }
    }

    private sealed class OneShotVoice(AudioClip clip, Fix64 volume)
    {
        public AudioClip Clip { get; } = clip;
        public Fix64 Volume { get; } = volume;
        public Fix64 Cursor { get; set; }
    }
}
