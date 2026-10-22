namespace BEngine.Audio;

public sealed partial class AudioSource
{
    private AudioClockSource? _independent;
    private AudioClockParameters _lastParameters;
    internal int IndependentRealVoices => _independent?.RealVoices ?? 0;
    private bool HasPendingIndependentCommand => _independent is { } state &&
        Volatile.Read(ref state.AppliedGeneration) != state.RequestedGeneration;

    internal bool CanUseIndependentClock(out string? reason)
    {
        if (!HasPendingIndependentCommand && _independent is { } state) _playing = state.MainPlaying;
        reason = null;
        if (mixerController is not null) { reason = "Mixer buses, sends and DSP retain the frame-clock path."; return false; }
        string? busReason;
        if (!CanUseIndependentBus(outputAudioMixerGroup, out busReason))
        { reason = busReason; return false; }
        reason = busReason;
        if (_playing && clip is { } current)
        {
            // BeginFrame must never synchronously decode an unloaded clip just
            // to qualify it for the independent clock. Let normal loading
            // finish first so the first frame does not pay the decode cost.
            if (current.loadState != AudioDataLoadState.Loaded)
            { reason = "An audio clip is not ready for independent playback."; return false; }
            if (!current.TryCapturePlaybackData(out _))
            { reason = "A clip is loading, unavailable or uses a main-thread procedural callback."; return false; }
        }
        foreach (var voice in _oneShots)
        {
            if (voice.Clip.loadState != AudioDataLoadState.Loaded)
            { reason = "A one-shot clip is not ready for independent playback."; return false; }
            if (!voice.Clip.TryCapturePlaybackData(out _))
            { reason = "A one-shot clip is loading, unavailable or uses a main-thread procedural callback."; return false; }
        }
        reason = null; return true;
    }

    private static bool CanUseIndependentBus(AudioMixerGroup? group, out string? reason)
    {
        // A plain authored bus is only a gain multiplier. Keep its routing value
        // exact and opt in; effects, sends and animated controller gains retain
        // the legacy graph because they need frame-owned DSP state.
        for (var current = group; current is not null; current = current.parent)
        {
            if (current.isDestroyed || current.effects is null || current.sends is null ||
                current.effects.Count != 0 || current.sends.Count != 0 || current.effectiveVolume != Fix64.One)
            { reason = "Mixer buses, sends and DSP retain the frame-clock path."; return false; }
        }
        reason = null; return true;
    }
    private AudioClockParameters CaptureIndependentParameters() =>
        new(_volume, panStereo, pitch, loop, mute || !enabled || !gameObject.activeInHierarchy, priority, GetInstanceID());

    internal AudioClockSource AttachIndependent(AudioClockSession session)
    {
        var state = new AudioClockSource(session, CaptureIndependentParameters())
        {
            Paused = _paused, Delay = _delay, Fading = _fading, StopAfterFade = _stopAfterFade,
            FadeStart = _fadeStart, FadeTarget = _fadeTarget, FadeDuration = _fadeDuration, FadeElapsed = _fadeElapsed
        };
        state.Main.Cursor = _sampleCursor; state.Main.Playing = _playing;
        if (_playing && clip is { } current && current.TryCapturePlaybackData(out var mainData)) state.Main.Data = mainData;
        _reader?.Dispose(); _reader = null;
        foreach (var voice in _oneShots)
        {
            voice.Reader?.Dispose(); voice.Reader = null;
            if (voice.Clip.TryCapturePlaybackData(out var data))
            {
                var prepared = new AudioClockVoice { Data = data, Cursor = voice.Cursor, Volume = voice.Volume, Id = voice.Id, Playing = true };
                voice.IndependentVoice = prepared; state.OneShots.Add(prepared);
            }
        }
        state.Publish(); _lastParameters = state.Parameters; _independent = state;
        if (!session.Enqueue(new(AudioClockCommandKind.Add, state)))
        {
            _independent = null; state.Detached = true; state.CloseReaders();
            throw new InvalidOperationException("Independent mixer command queue is full.");
        }
        return state;
    }
    internal void SynchronizeIndependent(AudioClockSource state)
    {
        RemoveCompletedIndependentVoices();
        if (_playing && clip is { } current && current.TryCapturePlaybackData(out var playback) &&
            !ReferenceEquals(state.MainData, playback) && !ReferenceEquals(state.RequestedData, playback))
        {
            if (SendIndependent(new(AudioClockCommandKind.Play, Data: playback, Flag: true)))
                state.RequestedData = playback;
            else return;
        }
        var parameters = CaptureIndependentParameters();
        if (parameters == _lastParameters) return;
        // A pan/priority/mute change must not overwrite an independently
        // progressing fade with the last owner-thread volume snapshot.
        if (SendIndependent(new(AudioClockCommandKind.Parameters, Parameters: parameters,
            Flag: parameters.Volume != _lastParameters.Volume))) _lastParameters = parameters;
    }
    private void RemoveCompletedIndependentVoices()
    {
        for (var i = _oneShots.Count - 1; i >= 0; i--)
            if (_oneShots[i].IndependentVoice is { } voice && !Volatile.Read(ref voice.Playing)) _oneShots.RemoveAt(i);
    }
    private bool SendIndependent(AudioClockCommand command)
    {
        if (_independent is not { } state) return false;
        var generation = ++state.RequestedGeneration;
        try
        {
            if (state.Session.Enqueue(command with { Source = state, Generation = generation })) return true;
        }
        catch (InvalidOperationException)
        {
            // A failed command must select the frame-clock path without
            // surfacing an exception from a gameplay setter or callback.
        }
        AudioIndependentClock.Shutdown(); return false;
    }
    private void IndependentPlay(Fix64 delay)
    {
        if (_independent is null) return;
        if (clip is not { } current || !current.TryCapturePlaybackData(out var data))
        { AudioIndependentClock.Shutdown(); _sampleCursor = 0; _paused = false; _playing = true; _delay = delay; return; }
        SendIndependent(new(AudioClockCommandKind.Play, Data: data, A: delay));
    }
    private void IndependentOneShot(OneShotVoice voice)
    {
        if (_independent is null) return;
        if (!voice.Clip.TryCapturePlaybackData(out var data)) { AudioIndependentClock.Shutdown(); return; }
        voice.IndependentVoice = new() { Data = data, Playing = true, Volume = voice.Volume, Id = voice.Id };
        if (!SendIndependent(new(AudioClockCommandKind.OneShot, Voice: voice.IndependentVoice)))
            voice.IndependentVoice = null;
    }
    internal void DetachIndependent(bool stopped = false)
    {
        if (_independent is not { } state) return;
        if (!stopped && !state.Session.Remove(state))
        {
            // The mixer could not acknowledge removal within its bounded
            // handoff window. Do not read mutable mixer fields while it may
            // still be rendering; the next frame-clock pass starts silent and
            // the audio thread owns final reader cleanup.
            _independent = null; _playing = false; _paused = false; _oneShots.Clear();
            return;
        }
        _independent = null;
        _sampleCursor = state.Main.Cursor; _playing = state.Main.Playing; _paused = state.Paused;
        _volume = state.Parameters.Volume; _delay = state.Delay;
        _fading = state.Fading; _stopAfterFade = state.StopAfterFade;
        _fadeStart = state.FadeStart; _fadeTarget = state.FadeTarget; _fadeDuration = state.FadeDuration; _fadeElapsed = state.FadeElapsed;
        for (var i = _oneShots.Count - 1; i >= 0; i--)
        {
            var voice = _oneShots[i];
            if (voice.IndependentVoice is not { } prepared) continue;
            voice.IndependentVoice = null;
            if (!prepared.Playing) _oneShots.RemoveAt(i);
            else { voice.Cursor = prepared.Cursor; voice.WasVirtual = false; }
        }
    }
}
