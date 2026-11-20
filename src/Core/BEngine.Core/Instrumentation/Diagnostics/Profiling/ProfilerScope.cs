namespace BEngine.Profiling;

public readonly struct ProfilerScope : IDisposable
{
    private readonly long _token;
    private readonly Profiler.RecorderRegistration? _registration;

    internal ProfilerScope(long token, Profiler.RecorderRegistration registration) { _token = token; _registration = registration; }

    public void Dispose() => _registration?.Recorder?.EndSample(_token);
}
