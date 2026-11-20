namespace BEngine.Editor;
public static class EditorBuildEvents
{
    public static event Action<string>? buildStarted;
    public static event Action<string, bool>? buildFinished;

    /// <summary>
    /// Starts one build operation. Complete the returned session after all work and
    /// cleanup succeeds; disposing an unfinished session reports failure once.
    /// Nested operations retain their own start/finish pair.
    /// </summary>
    public static EditorBuildSession BeginBuild(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var session = new EditorBuildSession(outputPath);
        try
        {
            RaiseBuildStarted(outputPath);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    internal static void RaiseBuildStarted(string outputPath)
    {
        EditorCallbackDispatcher.Invoke(buildStarted, outputPath, nameof(buildStarted));
    }

    internal static void RaiseBuildFinished(string outputPath, bool succeeded) =>
        EditorCallbackDispatcher.Invoke(buildFinished, outputPath, succeeded, nameof(buildFinished));
}
