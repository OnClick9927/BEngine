namespace BEngine.Launcher;

internal static class GitHubPackageDiagnostics
{
    private static readonly object Gate = new();
    internal static void Write(string context, Exception error)
    {
        try
        {
            lock (Gate) File.AppendAllText(Path.Combine(LauncherDataPaths.LogsPath, "HubGitHubPackages.log"),
                $"[{DateTimeOffset.Now:O}] {context}\n{error}\n\n");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Trace.WriteLine(failure); }
    }
}
