namespace BEngine.Editor;

/// <summary>Completes an editor frame when disposed.</summary>
public readonly struct EditorProfilerFrameScope : IDisposable
{
    private readonly long _token;

    /// <summary>Creates a scope for a frame token allocated by an IEditorProfilerSink implementation.</summary>
    public EditorProfilerFrameScope(long token) => _token = token;

    public void Dispose() => EditorProfiler.EndFrame(_token);
}
