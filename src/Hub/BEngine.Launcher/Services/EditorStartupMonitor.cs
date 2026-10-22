using System.Diagnostics;

namespace BEngine.Launcher;

internal static class EditorStartupMonitor
{
    internal static async Task<EditorStartupOutcome> WaitAsync(
        Process process,
        string statusPath,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        TimeSpan? totalTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentException.ThrowIfNullOrWhiteSpace(statusPath);
        var maximumWait = timeout ?? TimeSpan.FromMinutes(2);
        var maximumTotalWait = totalTimeout ?? TimeSpan.FromMinutes(10);
        var polling = pollInterval ?? TimeSpan.FromMilliseconds(100);
        var started = Stopwatch.GetTimestamp();
        var lastProgress = started;
        var lastRevision = -1L;
        string? lastMessage = null;

        while (true)
        {
            if (TryReadStatus(statusPath, out var status) && status.ProcessId == process.Id)
            {
                if (status.State.Equals("ready", StringComparison.OrdinalIgnoreCase))
                    return new EditorStartupOutcome(EditorStartupOutcomeKind.Ready, "Editor is ready.");
                if (status.State.Equals("failed", StringComparison.OrdinalIgnoreCase))
                    return new EditorStartupOutcome(EditorStartupOutcomeKind.Failed,
                        string.IsNullOrWhiteSpace(status.Message)
                            ? "The Editor reported a startup failure."
                            : status.Message,
                        status.LogPath);
                if (status.State.Equals("starting", StringComparison.OrdinalIgnoreCase) &&
                    status.Revision > lastRevision)
                {
                    lastRevision = status.Revision;
                    lastProgress = Stopwatch.GetTimestamp();
                    lastMessage = status.Message;
                }
            }

            if (process.HasExited)
            {
                var exitCode = process.ExitCode;
                return new EditorStartupOutcome(EditorStartupOutcomeKind.UnexpectedExit,
                    $"The Editor process exited before its first frame (exit code {FormatExitCode(exitCode)}).",
                    ExitCode: exitCode);
            }

            if (Stopwatch.GetElapsedTime(started) >= maximumTotalWait)
                return new EditorStartupOutcome(EditorStartupOutcomeKind.TimedOut,
                    $"The Editor did not report a ready first frame within {maximumTotalWait.TotalSeconds:0} seconds. " +
                    $"Last startup step: {lastMessage ?? "waiting for the Editor"}.");
            if (Stopwatch.GetElapsedTime(lastProgress) >= maximumWait)
                return new EditorStartupOutcome(EditorStartupOutcomeKind.TimedOut,
                    $"The Editor reported no startup progress for {maximumWait.TotalSeconds:0} seconds. " +
                    $"Last startup step: {lastMessage ?? "waiting for the Editor"}. " +
                    "The process may still be running or blocked during startup.");

            await Task.Delay(polling).ConfigureAwait(false);
        }
    }

    internal static bool TryReadStatus(string path, out EditorStartupStatus status)
    {
        status = default!;
        try
        {
            if (!File.Exists(path)) return false;
            status = YamlUtility.Deserialize<EditorStartupStatus>(File.ReadAllText(path));
            return status is not null && !string.IsNullOrWhiteSpace(status.State);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static void TryDeleteStatus(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string FormatExitCode(int exitCode) =>
        $"{exitCode} / 0x{unchecked((uint)exitCode):X8}";

    internal sealed record EditorStartupStatus(
        string State,
        string? Message,
        string? LogPath,
        int ProcessId,
        DateTimeOffset TimestampUtc,
        long Revision = 0)
    {
        public EditorStartupStatus() : this(string.Empty, null, null, 0, default) { }
    }
}
