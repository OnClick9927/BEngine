namespace BEngine.Audio;

/// <summary>Quality and latency limits; zero real voices selects the legacy unlimited mixing mode.</summary>
public static class AudioRuntimeSettings
{
    /// <summary>Opt-in independent 5ms mix clock. Unsupported outputs, procedural
    /// callbacks or authored mixer buses use the unchanged frame-clock path.</summary>
    public static bool useIndependentClock { get; set; }
    public static IndependentAudioSnapshot independentClockSnapshot => AudioIndependentClock.Snapshot;
    public static AudioVoiceSnapshot voiceSnapshot { get; internal set; }
    private static int _realVoices = 64, _oneShots = 32, _bufferMilliseconds = 120;
    public static int maximumRealVoices
    { get => _realVoices; set { if (value is < 0 or > 65536) throw new ArgumentOutOfRangeException(nameof(value)); _realVoices = value; } }
    public static int maximumOneShotsPerSource
    { get => _oneShots; set { if (value is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(value)); _oneShots = value; } }
    /// <summary>Applied when a system output is created. Reset the output to apply a new value.</summary>
    public static int outputBufferMilliseconds
    { get => _bufferMilliseconds; set { if (value is < 20 or > 2000) throw new ArgumentOutOfRangeException(nameof(value)); _bufferMilliseconds = value; } }
    public static long rejectedOneShots { get; internal set; }
}

public readonly record struct AudioVoiceSnapshot(int LogicalVoices, int RealVoices, int VirtualVoices);
