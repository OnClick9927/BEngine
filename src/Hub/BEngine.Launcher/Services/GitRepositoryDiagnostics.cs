using System.Text.RegularExpressions;

namespace BEngine.Launcher;

internal sealed class GitCommandException(string phase, bool timedOut, TimeSpan timeout, string detail)
    : IOException((timedOut ? "Git command timed out" : "Git command failed") + " · " + phase + " · " + detail)
{
    internal string Phase { get; } = phase;
    internal bool TimedOut { get; } = timedOut;
    internal TimeSpan Timeout { get; } = timeout;
    internal string Detail { get; } = detail;
}

internal static class GitRepositoryDiagnostics
{
    private static readonly object Gate = new();
    internal static string Redact(string value) => Regex.Replace(value, @"(https?://)[^/\s@]+@", "$1<redacted>@", RegexOptions.IgnoreCase);
    internal static void Write(string root, string message)
    {
        try
        {
            var temp = System.IO.Directory.GetParent(root)?.Parent?.FullName;
            if (temp is null) return;
            var logs = Path.Combine(temp, "Logs"); System.IO.Directory.CreateDirectory(logs);
            lock (Gate) File.AppendAllText(Path.Combine(logs, "HubGit.log"), $"[{DateTimeOffset.Now:O}] {Path.GetFileName(root)} · {Redact(message)}\n");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(error.Message); }
    }
}
