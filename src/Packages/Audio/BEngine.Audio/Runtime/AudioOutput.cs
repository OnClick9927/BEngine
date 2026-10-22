namespace BEngine.Audio;

public static class AudioOutput
{
    private static IAudioOutput? _current;
    internal static IAudioOutput? initializedOutput => _current;
    private static float[]? _frameSamples;
    private static int _frameLength;
    private static IAudioOutput? _frameOutput;

    public static IAudioOutput current
    {
        get => _current ??= AudioRuntimeServices.OutputFactory.CreateDefault();
        set { ArgumentNullException.ThrowIfNull(value); AudioIndependentClock.Shutdown(); _current = value; }
    }

    public static bool isSystemOutputAvailable =>
        AudioRuntimeServices.OutputFactory.IsSystemOutput(current);

    public static void ResetToSystemDefault()
    {
        AudioIndependentClock.Shutdown();
        var replacement = AudioRuntimeServices.OutputFactory.CreateDefault();
        var previous = _current;
        _current = replacement;
        if (previous is not null && !ReferenceEquals(previous, replacement))
            (previous as IDisposable)?.Dispose();
    }

    public static void Shutdown()
    {
        AudioIndependentClock.Shutdown();
        _frameSamples = null;
        _frameLength = 0; _frameOutput = null; _backpressureReported = false;
        var replacement = AudioRuntimeServices.OutputFactory.SilentOutput;
        var previous = _current;
        _current = replacement;
        if (previous is not null && !ReferenceEquals(previous, replacement))
            (previous as IDisposable)?.Dispose();
    }

    internal static void BeginMixFrame()
    {
        if (_frameOutput is not null)
            throw new InvalidOperationException("An audio mix frame is already active.");
        _frameOutput = current;
        _frameLength = 0;
    }

    internal static void SubmitMix(ReadOnlySpan<float> samples, IAudioOutput output)
    {
        if (_frameOutput is null)
        {
            ApplyMasterGain(samples, output);
            return;
        }
        if (!ReferenceEquals(output, _frameOutput))
            throw new InvalidOperationException("The audio output changed during a runtime frame.");
        if (samples.IsEmpty) return;
        if (_frameSamples is null || _frameSamples.Length < samples.Length)
            Array.Resize(ref _frameSamples, Math.Max(samples.Length, _frameSamples?.Length * 2 ?? 1024));
        if (samples.Length > _frameLength)
            _frameSamples.AsSpan(_frameLength, samples.Length - _frameLength).Clear();
        using (LoopExecutionGuard.AllowIterations(samples.Length + 1))
            for (var index = 0; index < samples.Length; index++) _frameSamples[index] += samples[index];
        _frameLength = Math.Max(_frameLength, samples.Length);
    }

    internal static void EndMixFrame()
    {
        var output = _frameOutput;
        try
        {
            if (output is not null && _frameLength > 0)
                ApplyMasterGain(_frameSamples.AsSpan(0, _frameLength), output);
        }
        finally
        {
            if (_frameSamples is not null && _frameLength > 0)
                _frameSamples.AsSpan(0, _frameLength).Clear();
            _frameLength = 0;
            _frameOutput = null;
        }
    }

    private static void ApplyMasterGain(ReadOnlySpan<float> samples, IAudioOutput output)
    {
        if (samples.IsEmpty) return;
        var adjusted = System.Buffers.ArrayPool<float>.Shared.Rent(samples.Length);
        try
        {
        var volume = (float)Mathf.Clamp01(AudioListener.volume);
        using (LoopExecutionGuard.AllowIterations(samples.Length + 1))
            for (var index = 0; index < samples.Length; index++)
            {
                var sample = samples[index] * volume;
                adjusted[index] = float.IsFinite(sample) ? sample : 0;
            }
        if (output is IAudioBufferedOutput buffered)
        {
            if (!buffered.TrySubmit(adjusted.AsSpan(0, samples.Length), out var accepted) && accepted < samples.Length / output.channelCount)
            {
                backpressuredFrames += samples.Length / output.channelCount - accepted;
                if (!_backpressureReported) Debug.LogWarning("Audio output applied backpressure; increase the configured prebuffer or reduce frame stalls.");
                _backpressureReported = true;
            }
            else _backpressureReported = false;
        }
        else output.Submit(adjusted.AsSpan(0, samples.Length));
        }
        finally { System.Buffers.ArrayPool<float>.Shared.Return(adjusted); }
    }
    public static long backpressuredFrames { get; private set; }
    private static bool _backpressureReported;
}
