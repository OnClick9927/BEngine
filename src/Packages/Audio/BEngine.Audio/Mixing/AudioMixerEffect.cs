namespace BEngine.Audio;

public enum AudioMixerEffectType
{
    Gain, LowPass, HighPass, Delay, Compressor,
    ParametricEqualizer, Distortion, Chorus, ConvolutionReverb, Plugin
}

/// <summary>Authored settings; every scene/controller/bus owns independent DSP history.</summary>
public sealed class AudioMixerEffect
{
    public AudioMixerEffectType type { get; set; }
    public bool enabled { get; set; } = true;
    [Range(-80, 24)] public Fix64 gainDb { get; set; }
    [Range(10, 24000)] public Fix64 cutoffHz { get; set; } = 1000;
    [Range(0.1f, 10)] public Fix64 resonance { get; set; } = (Fix64).70710678;
    [Range(0.001f, 2)] public Fix64 delaySeconds { get; set; } = (Fix64).25;
    [Range(0, 0.95f)] public Fix64 feedback { get; set; } = (Fix64).3;
    [Range(0, 1)] public Fix64 wet { get; set; } = (Fix64).5;
    [Range(-80, 0)] public Fix64 thresholdDb { get; set; } = -12;
    [Range(1, 20)] public Fix64 ratio { get; set; } = 4;
    [Range(0, 1)] public Fix64 attackSeconds { get; set; } = (Fix64).01;
    [Range(0, 2)] public Fix64 releaseSeconds { get; set; } = (Fix64).1;
    [Range(1, 50)] public Fix64 drive { get; set; } = 2;
    [Range(0.01f, 20)] public Fix64 modulationRateHz { get; set; } = (Fix64).8;
    [Range(0, 0.05f)] public Fix64 modulationDepthSeconds { get; set; } = (Fix64).003;
    public int impulseResponseSampleRate { get; set; } = 48000;
    public List<float> impulseResponse { get; set; } = [];
    public string pluginId { get; set; } = string.Empty;
    public List<float> pluginParameters { get; set; } = [];
    public AudioMixerEffect Clone()
    {
        var clone = (AudioMixerEffect)MemberwiseClone();
        clone.impulseResponse = [.. impulseResponse];
        clone.pluginParameters = [.. pluginParameters];
        return clone;
    }

    public void Validate()
    {
        if (!Enum.IsDefined(type)) throw new InvalidDataException("Unknown audio effect type.");
        Check(gainDb, -80, 24, nameof(gainDb)); Check(cutoffHz, 10, 24000, nameof(cutoffHz));
        Check(resonance, (Fix64).1, 10, nameof(resonance)); Check(delaySeconds, (Fix64).001, 2, nameof(delaySeconds));
        Check(feedback, 0, (Fix64).95, nameof(feedback)); Check(wet, 0, 1, nameof(wet));
        Check(thresholdDb, -80, 0, nameof(thresholdDb)); Check(ratio, 1, 20, nameof(ratio));
        Check(attackSeconds, 0, 1, nameof(attackSeconds)); Check(releaseSeconds, 0, 2, nameof(releaseSeconds));
        Check(drive, 1, 50, nameof(drive)); Check(modulationRateHz, (Fix64).01, 20, nameof(modulationRateHz));
        Check(modulationDepthSeconds, 0, (Fix64).05, nameof(modulationDepthSeconds));
        if (impulseResponseSampleRate is < 8000 or > 192000)
            throw new InvalidDataException("Audio effect impulseResponseSampleRate must be in [8000, 192000].");
        if (impulseResponse.Count > 4096) throw new InvalidDataException("Convolution reverb is limited to 4096 mono taps.");
        if (impulseResponse.Any(sample => !float.IsFinite(sample) || Math.Abs(sample) > 8))
            throw new InvalidDataException("Convolution reverb taps must be finite and in [-8, 8].");
        if (pluginParameters.Count > 256 || pluginParameters.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("DSP plugin parameters must be finite, with at most 256 values.");
    }
    private static void Check(Fix64 value, Fix64 minimum, Fix64 maximum, string property)
    { if (value < minimum || value > maximum) throw new InvalidDataException($"Audio effect {property} must be in [{minimum}, {maximum}]."); }
}

public sealed class AudioMixerSend
{
    public AudioMixerGroup? target { get; set; }
    [Range(0, 1)] public Fix64 volume { get; set; } = Fix64.One;
    public bool preFader { get; set; }
}
