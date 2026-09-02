namespace BEngine.Audio;

public static class AudioOutput
{
    private static IAudioOutput _current = CreateSystemDefault();

    public static IAudioOutput current
    {
        get => Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value ?? throw new ArgumentNullException(nameof(value)));
    }

    public static bool isSystemOutputAvailable => current is OpenAlAudioOutput;

    public static void ResetToSystemDefault()
    {
        var replacement = CreateSystemDefault();
        var previous = Interlocked.Exchange(ref _current, replacement);
        if (!ReferenceEquals(previous, NullAudioOutput.Instance)) (previous as IDisposable)?.Dispose();
    }

    public static void Shutdown()
    {
        var previous = Interlocked.Exchange(ref _current, NullAudioOutput.Instance);
        if (!ReferenceEquals(previous, NullAudioOutput.Instance)) (previous as IDisposable)?.Dispose();
    }

    private static IAudioOutput CreateSystemDefault() =>
        OpenAlAudioOutput.TryCreate(out var output) ? output! : NullAudioOutput.Instance;

    private sealed class NullAudioOutput : IAudioOutput
    {
        internal static NullAudioOutput Instance { get; } = new();
        public int sampleRate => 48000;
        public int channelCount => 2;
        public void Submit(ReadOnlySpan<float> interleavedSamples) { }
    }
}
