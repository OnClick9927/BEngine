namespace BEngine.Editor;

/// <summary>Default workbench presentation policy for Core's loop-protection notification.</summary>
internal static class EditorLoopProtection
{
    internal static void OnLoopStopped(LoopExecutionLimitException exception)
    {
        if (EditorApplication.isPlaying) EditorApplication.isPaused = true;
        EditorUtility.DisplayDialog("Script Execution Stopped",
            $"BEngine stopped a script loop after {exception.IterationLimit} iterations so the editor can remain responsive.\n\nEntry: {exception.EntryPoint}\nSource: {exception.SourcePath}:{exception.Line}\n\nCorrect the loop condition and let script hot reload compile the change.", "OK");
    }
}
