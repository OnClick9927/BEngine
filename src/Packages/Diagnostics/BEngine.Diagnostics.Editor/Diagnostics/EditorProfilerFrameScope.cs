namespace BEngine.Editor;

/// <summary>Completes an editor frame when disposed.</summary>
public readonly struct EditorProfilerFrameScope : IDisposable
{
    private readonly long _token;

    internal EditorProfilerFrameScope(long token) => _token = token;

    public void Dispose() => EditorProfiler.EndFrame(_token);
}
