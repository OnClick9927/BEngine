namespace BEngine.Audio;

[AddComponentMenu("Audio/Audio Source")]
public sealed partial class AudioSource : MonoBehaviour
{
    [NonSerialized] private Fix64 _sampleCursor;
    [NonSerialized] private bool _playing;
    [NonSerialized] private bool _paused;
    [NonSerialized] private Fix64 _delay;
    [NonSerialized] private readonly List<OneShotVoice> _oneShots = [];
    [NonSerialized] private AudioVoiceReader? _reader;
    private static readonly Fix64 MinimumPitch = Fix64.Parse("0.01");
    [NonSerialized] private bool _wasVirtual;
    private AudioClip? _clip;
    [NonSerialized] private Fix64 _fadeStart, _fadeTarget, _fadeDuration, _fadeElapsed;
    [NonSerialized] private bool _fading, _stopAfterFade;

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
    private Fix64 _volume = Fix64.One;
    [Range(0, 1)] public Fix64 volume
    {
        get => !HasPendingIndependentCommand && _independent is { } state ? state.CurrentVolume : _volume;
        set
        {
            _volume = value;
            if (_independent is null) return;
            var parameters = CaptureIndependentParameters();
            SendIndependent(new(AudioClockCommandKind.Parameters, Parameters: parameters, Flag: true));
            _lastParameters = parameters;
        }
    }
    [Range(-1, 1)] public Fix64 panStereo { get; set; }
    [Range(0.01f, 3)] public Fix64 pitch { get; set; } = Fix64.One;
    public bool loop { get; set; }
    public bool playOnAwake { get; set; } = true;
    public bool mute { get; set; }
    public AudioMixerGroup? outputAudioMixerGroup { get; set; }
    public AudioMixerController? mixerController { get; set; }
    public bool isFading => !HasPendingIndependentCommand && _independent is { } state ? state.IsFading : _fading;
    [Range(0, 256)] public int priority { get; set; } = 128;
    public bool isPlaying => !HasPendingIndependentCommand && _independent is { } state ? state.IsPlaying : (_playing || _oneShots.Count > 0) && !_paused;
    public int timeSamples
    {
        get => Mathf.FloorToInt(!HasPendingIndependentCommand && _independent is { } state ? state.Position : _sampleCursor);
        set { _sampleCursor = Fix64.Clamp(value, 0, Math.Max(0, clip?.samples - 1 ?? 0)); SendIndependent(new(AudioClockCommandKind.Seek, A: _sampleCursor)); }
    }
    public Fix64 time
    {
        get => clip is { frequency: > 0 } value ? (!HasPendingIndependentCommand && _independent is { } state ? state.Position : _sampleCursor) / value.frequency : Fix64.Zero;
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
    public override void OnDestroy() => Stop();

    public void FadeTo(Fix64 targetVolume, Fix64 duration, bool stopWhenComplete = false)
    {
        if (duration < 0) throw new ArgumentOutOfRangeException(nameof(duration));
        _fadeStart = volume; _fadeTarget = Mathf.Clamp01(targetVolume);
        _fadeDuration = duration; _fadeElapsed = 0; _stopAfterFade = stopWhenComplete;
        _fading = duration > 0;
        if (!_fading) { volume = _fadeTarget; if (stopWhenComplete) Stop(); }
        SendIndependent(new(AudioClockCommandKind.Fade, A: _fadeTarget, B: duration, Flag: stopWhenComplete));
    }

    public void CancelFade() { _fading = false; SendIndependent(new(AudioClockCommandKind.CancelFade)); }

    public void Play()
    {
        if (clip is not { isDestroyed: false }) return;
        _reader?.Dispose(); _reader = null;
        _sampleCursor = Fix64.Zero;
        _delay = Fix64.Zero;
        _paused = false;
        _playing = true;
        IndependentPlay(0);
    }

    public void PlayDelayed(Fix64 delay)
    {
        if (delay < Fix64.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        if (_independent is null) Play();
        else { _sampleCursor = 0; _paused = false; _playing = true; IndependentPlay(delay); }
        _delay = delay;
    }

    public void PlayOneShot(AudioClip oneShotClip) => PlayOneShot(oneShotClip, Fix64.One);

    public void PlayOneShot(AudioClip oneShotClip, Fix64 volumeScale)
    {
        ArgumentNullException.ThrowIfNull(oneShotClip);
        TryPlayOneShot(oneShotClip, volumeScale);
    }

    public bool TryPlayOneShot(AudioClip oneShotClip, Fix64 volumeScale)
    {
        ArgumentNullException.ThrowIfNull(oneShotClip);
        RemoveCompletedIndependentVoices();
        if (_oneShots.Count >= AudioRuntimeSettings.maximumOneShotsPerSource)
        { AudioRuntimeSettings.rejectedOneShots++; return false; }
        var voice = new OneShotVoice(oneShotClip,
            Fix64.Clamp(volumeScale, Fix64.Zero, Fix64.One)) { Id = ++_nextVoice };
        _oneShots.Add(voice); IndependentOneShot(voice);
        return true;
    }

    public void Pause()
    {
        _paused = _playing || _oneShots.Count > 0;
        _reader?.Dispose(); _reader = null;
        foreach (var voice in _oneShots) { voice.Reader?.Dispose(); voice.Reader = null; }
        SendIndependent(new(AudioClockCommandKind.Pause));
    }
    public void UnPause() { _paused = false; SendIndependent(new(AudioClockCommandKind.Resume)); }

    public void Stop()
    {
        _fading = false;
        _playing = false;
        _realMain = false; _wasVirtual = false;
        _paused = false;
        _delay = Fix64.Zero;
        _sampleCursor = Fix64.Zero;
        _reader?.Dispose(); _reader = null;
        foreach (var voice in _oneShots) voice.Reader?.Dispose();
        _oneShots.Clear();
        SendIndependent(new(AudioClockCommandKind.Stop));
    }

    internal void Mix(Span<float> output, int outputSampleRate, int outputChannels, Fix64 deltaTime,
        bool applyMixerGain = true, bool useVoiceSelection = false, int virtualFrames = 0, bool retainSkippedDecoder = false)
    {
        if (_paused || AudioListener.pause || !enabled || !gameObject.activeInHierarchy) return;
        if (_fading)
        {
            _fadeElapsed += deltaTime;
            volume = Mathf.Lerp(_fadeStart, _fadeTarget, Mathf.Clamp01(_fadeElapsed / _fadeDuration));
            if (_fadeElapsed >= _fadeDuration)
            {
                _fading = false;
                if (_stopAfterFade) Stop();
            }
        }
        var gain = mute ? Fix64.Zero : Fix64.Clamp(volume, Fix64.Zero, Fix64.One) * (applyMixerGain ? AudioListener.volume : Fix64.One) *
            (!applyMixerGain ? Fix64.One : mixerController is { isDestroyed: false, enabled: true } mixer &&
             mixer.gameObject.activeInHierarchy && ReferenceEquals(mixer.gameObject.scene, gameObject.scene)
                ? mixer.GetEffectiveVolume(outputAudioMixerGroup)
                : outputAudioMixerGroup?.effectiveVolume ?? Fix64.One);
        var pan = Fix64.Clamp(panStereo, -Fix64.One, Fix64.One);
        if (_playing && !_paused && clip is { } current)
        {
            var delayedFrames = 0;
            if (_delay > Fix64.Zero)
            {
                delayedFrames = Math.Min(virtualFrames > 0 ? virtualFrames : output.Length / outputChannels, Mathf.CeilToInt(_delay * outputSampleRate));
                _delay = Fix64.Max(0, _delay - (Fix64)delayedFrames / outputSampleRate);
            }
            MixVoice(output[(virtualFrames > 0 ? 0 : delayedFrames * outputChannels)..], outputSampleRate, outputChannels, current, gain, pan,
                ref _sampleCursor, loop, ref _playing, ref _reader, ref _wasVirtual, virtualFrames == 0 && (!useVoiceSelection || _realMain),
                Math.Max(0, virtualFrames - delayedFrames), retainSkippedDecoder);
        }

        for (var index = _oneShots.Count - 1; index >= 0; index--)
        {
            var voice = _oneShots[index];
            var playing = true;
            var cursor = voice.Cursor;
            MixVoice(output, outputSampleRate, outputChannels, voice.Clip, gain * voice.Volume, pan,
                ref cursor, false, ref playing, ref voice.Reader, ref voice.WasVirtual, virtualFrames == 0 && (!useVoiceSelection || voice.Real), virtualFrames, retainSkippedDecoder);
            voice.Cursor = cursor;
            if (!playing) _oneShots.RemoveAt(index);
            else _oneShots[index] = voice;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    private void MixVoice(Span<float> output, int outputSampleRate, int outputChannels,
        AudioClip source, Fix64 gain, Fix64 pan, ref Fix64 cursor, bool repeat, ref bool playing,
        ref AudioVoiceReader? reader, ref bool wasVirtual, bool real = true, int virtualFrames = 0, bool retainSkippedDecoder = false,
        bool throwOnFailure = false)
    {
        if (reader is not null && (!reader.IsCurrent || source.isDestroyed)) { reader.Dispose(); reader = null; }
        if (!source.isDestroyed && source.loadState == AudioDataLoadState.Unloaded) source.LoadAudioData();
        if (!source.isDestroyed && source.loadState == AudioDataLoadState.Loading) { if (real) source.PrioritizeLoading(); return; }
        if (source.isDestroyed || source.samples <= 0 || source.loadState != AudioDataLoadState.Loaded)
        {
            if (!source.isDestroyed && source.loadState == AudioDataLoadState.Failed) Debug.LogError($"Audio load failed for '{source.name}': {source.loadError}");
            playing = false; reader?.Dispose(); reader = null; return;
        }
        if (output.IsEmpty && virtualFrames == 0) return;
        if (!real || gain <= 0)
        {
            reader?.WriteFadeOut(output, outputChannels);
            // Output backpressure skips time, but does not evict the selected
            // voice. Reopening a compressed track here decodes from its start
            // at every short queue saturation. Actual virtual-voice eviction
            // still releases the decoder and its bounded window.
            if (!retainSkippedDecoder)
            {
                if (reader is not null) AudioPlaybackDiagnostics.Mark(real ? AudioPlaybackDiagnostics.Operation.MutedEvict : AudioPlaybackDiagnostics.Operation.VirtualEvict,
                    source.name, Mathf.FloorToInt(cursor), -1);
                reader?.Dispose(); reader = null;
            }
            wasVirtual = true;
            var virtualStep = (Fix64)source.frequency / outputSampleRate * Fix64.Clamp(pitch, MinimumPitch, 3);
            cursor += virtualStep * (virtualFrames > 0 ? virtualFrames : output.Length / outputChannels);
            if (repeat) cursor %= source.samples;
            else if (cursor >= source.samples) { cursor = source.samples; playing = false; }
            if (!playing) { reader?.Dispose(); reader = null; }
            return;
        }
        try
        {
        if (reader is null || !reader.IsCurrent)
        {
            using var measurement = AudioPlaybackDiagnostics.Begin(wasVirtual ? AudioPlaybackDiagnostics.Operation.VoiceResume : AudioPlaybackDiagnostics.Operation.VoiceOpen,
                source.name, -1, Mathf.FloorToInt(cursor));
            reader?.Dispose(); reader = new AudioVoiceReader(source, wasVirtual); wasVirtual = false;
        }
        else if (wasVirtual) { reader.ResumeAfterSkip(); wasVirtual = false; }
        var frames = output.Length / outputChannels;
        using var loopBudget = LoopExecutionGuard.AllowIterations(frames);
        var step = (Fix64)source.frequency / outputSampleRate * Fix64.Clamp(pitch, MinimumPitch, 3);
        var leftGain = gain * (pan > Fix64.Zero ? Fix64.One - pan : Fix64.One);
        var rightGain = gain * (pan < Fix64.Zero ? Fix64.One + pan : Fix64.One);
        var leftAmplitude = (float)leftGain;
        var rightAmplitude = (float)rightGain;
        if (reader.TryMixPcm(output, outputChannels, step, leftAmplitude, rightAmplitude, repeat, ref cursor, out var ended))
        { if (ended) playing = false; return; }
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
            if (!reader.TryStereo(cursor, repeat, out var left, out var right)) break;
            if (outputChannels == 1)
                output[frame] += (left * leftAmplitude + right * rightAmplitude) * .5f;
            else
            {
                output[frame * outputChannels] += left * leftAmplitude;
                output[frame * outputChannels + 1] += right * rightAmplitude;
            }
            cursor += step;
            reader.LastLeft = left * leftAmplitude; reader.LastRight = right * rightAmplitude;
        }
        if (repeat) cursor %= source.samples;
        else if (cursor >= source.samples) { cursor = source.samples; playing = false; }
        }
        catch (Exception error)
        {
            playing = false;
            if (throwOnFailure) throw;
            Debug.LogError($"Audio playback failed for '{source.name}': {error.Message}");
        }
        finally { if (!playing) { reader?.Dispose(); reader = null; } }
    }

    internal sealed class OneShotVoice(AudioClip clip, Fix64 volume)
    {
        public AudioClip Clip { get; } = clip;
        public Fix64 Volume { get; } = volume;
        public Fix64 Cursor { get; set; }
        public AudioVoiceReader? Reader;
        public bool Real;
        public bool WasVirtual;
        public int Id;
        internal AudioClockVoice? IndependentVoice;
    }
}
