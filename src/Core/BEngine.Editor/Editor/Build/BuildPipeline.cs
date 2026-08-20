namespace BEngine.Editor;

public static class BuildPipeline
{
    public static event Action<string>? buildStarted;
    public static event Action<string, bool>? buildFinished;

    internal static void RaiseBuildStarted(string outputPath)
    {
        ConsoleLogController.ClearIfEnabled(ConsoleClearTrigger.Build);
        EditorCallbackDispatcher.Invoke(buildStarted, outputPath, nameof(buildStarted));
    }

    internal static void RaiseBuildFinished(string outputPath, bool succeeded) =>
        EditorCallbackDispatcher.Invoke(buildFinished, outputPath, succeeded, nameof(buildFinished));
}
