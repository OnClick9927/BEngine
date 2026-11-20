namespace BEngine.Editor;

/// <summary>Owns the balanced lifecycle of one editor build, including async work.</summary>
public sealed class EditorBuildSession : IDisposable
{
    private int _completed;

    internal EditorBuildSession(string outputPath) => OutputPath = outputPath;

    /// <summary>The caller's output identity, preserved without path rewriting.</summary>
    public string OutputPath { get; }

    /// <summary>Reports the outcome once. The first completion or disposal wins.</summary>
    public void Complete(bool succeeded)
    {
        // Set the state before callbacks, which may dispose or complete us again.
        if (Interlocked.Exchange(ref _completed, 1) == 0)
            EditorBuildEvents.RaiseBuildFinished(OutputPath, succeeded);
    }

    /// <summary>Reports failure if the operation has not explicitly completed.</summary>
    public void Dispose() => Complete(false);
}
