namespace BEngine.Editor;

/// <summary>Completes a profiler phase sample when disposed.</summary>
public readonly struct EditorProfilerSampleScope : IDisposable
{
    private readonly long _frameToken;
    private readonly EditorProfilerArea _area;
    private readonly long _startedTimestamp;

    internal EditorProfilerSampleScope(
        long frameToken, EditorProfilerArea area, long startedTimestamp)
    {
        _frameToken = frameToken;
        _area = area;
        _startedTimestamp = startedTimestamp;
    }

    public void Dispose() => EditorProfiler.EndSample(_frameToken, _area, _startedTimestamp);
}
