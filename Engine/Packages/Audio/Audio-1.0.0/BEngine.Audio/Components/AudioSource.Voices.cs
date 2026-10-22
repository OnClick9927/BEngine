namespace BEngine.Audio;

public sealed partial class AudioSource
{
    private bool _realMain;
    private int _nextVoice;
    public int activeVoiceCount => !HasPendingIndependentCommand && _independent is { } state ? (state.MainPlaying ? 1 : 0) + state.OneShotCount : (_playing ? 1 : 0) + _oneShots.Count;
    public bool isVirtual => isPlaying && (_independent is { } state ? state.RealVoices == 0 : !HasRealVoice);
    internal bool HasRealVoice { get { if (_realMain) return true; foreach (var voice in _oneShots) if (voice.Real) return true; return false; } }
    internal bool HasRetiringVoice { get { if (!_realMain && _reader is not null) return true; foreach (var voice in _oneShots) if (!voice.Real && voice.Reader is not null) return true; return false; } }

    internal void GatherVoices(FrameDataBuffer<AudioVoiceCandidate> candidates)
    {
        var wasReal = _realMain; _realMain = false;
        var gain = Mathf.Clamp01(volume);
        try { gain *= (mixerController is { isDestroyed: false, enabled: true } controller &&
            controller.gameObject.activeInHierarchy && ReferenceEquals(controller.gameObject.scene, gameObject.scene)
                ? controller.GetEffectiveVolume(outputAudioMixerGroup) : outputAudioMixerGroup?.effectiveVolume ?? Fix64.One); }
        // Update validates and reports authored routing errors once per scene.
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException) { gain = 0; }
        var audible = enabled && gameObject.activeInHierarchy && !_paused && !mute && gain > 0;
        if (_playing && audible) candidates.Add(new(this, null, Rank(gain, wasReal, 0)));
        foreach (var voice in _oneShots)
        {
            wasReal = voice.Real; voice.Real = false;
            if (audible && voice.Volume > 0) candidates.Add(new(this, voice, Rank(gain * voice.Volume, wasReal, voice.Id)));
        }
    }
    private (int, Fix64, int, int, int) Rank(Fix64 gain, bool wasReal, int voiceId) =>
        (-Math.Clamp(priority, 0, 256), Mathf.Clamp01(gain), wasReal ? 1 : 0, -GetInstanceID(), -voiceId);
    internal void SelectVoice(OneShotVoice? voice) { if (voice is null) _realMain = true; else voice.Real = true; }
    internal readonly record struct AudioVoiceCandidate(AudioSource Source, OneShotVoice? Voice,
        (int, Fix64, int, int, int) Rank);
}
