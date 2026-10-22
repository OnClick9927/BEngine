namespace BEngine.Profiling;

public readonly struct ProfilerScope : IDisposable
{
    private readonly long _token;

    internal ProfilerScope(long token) => _token = token;

    public void Dispose() => Profiler.EndSample(_token);
}
