namespace BEngine.Editor;

internal static class ConsoleLogController
{
    internal static bool ClearIfEnabled(ConsoleClearTrigger trigger)
    {
        var enabled = trigger switch
        {
            ConsoleClearTrigger.Play => ConsolePreferences.ClearOnPlay,
            ConsoleClearTrigger.Build => ConsolePreferences.ClearOnBuild,
            ConsoleClearTrigger.Recompile => ConsolePreferences.ClearOnRecompile,
            _ => false
        };
        if (!enabled) return false;
        EditorLogStore.Clear();
        return true;
    }

    internal static bool ShouldPauseOnError(LogEntry entry, bool isPlaying) =>
        isPlaying && ConsolePreferences.ErrorPause && entry.Type == LogType.Error;
}
