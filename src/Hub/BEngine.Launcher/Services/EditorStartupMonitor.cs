using System.Diagnostics;
using System.Text.Json;

namespace BEngine.Launcher;

internal static class EditorStartupMonitor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static async Task<EditorStartupOutcome> WaitAsync(
        Process process,
        string statusPath,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentException.ThrowIfNullOrWhiteSpace(statusPath);
        var maximumWait = timeout ?? TimeSpan.FromMinutes(2);
        var polling = pollInterval ?? TimeSpan.FromMilliseconds(100);
        var started = Stopwatch.GetTimestamp();

        while (Stopwatch.GetElapsedTime(started) < maximumWait)
        {
            if (TryReadStatus(statusPath, out var status))
            {
                if (status.State.Equals("ready", StringComparison.OrdinalIgnoreCase))
                    return new EditorStartupOutcome(EditorStartupOutcomeKind.Ready, "Editor is ready.");
                if (status.State.Equals("failed", StringComparison.OrdinalIgnoreCase))
                    return new EditorStartupOutcome(EditorStartupOutcomeKind.Failed,
                        string.IsNullOrWhiteSpace(status.Message)
                            ? "The Editor reported a startup failure."
                            : status.Message,
                        status.LogPath);
            }

            if (process.HasExited)
            {
                var exitCode = process.ExitCode;
                return new EditorStartupOutcome(EditorStartupOutcomeKind.UnexpectedExit,
                    $"The Editor process exited before its first frame (exit code {FormatExitCode(exitCode)}).",
                    ExitCode: exitCode);
            }

            await Task.Delay(polling).ConfigureAwait(false);
        }

        return new EditorStartupOutcome(EditorStartupOutcomeKind.TimedOut,
            $"The Editor did not report a ready first frame within {maximumWait.TotalSeconds:0} seconds. " +
            "The process may still be running or blocked during startup.");
    }

    internal static bool TryReadStatus(string path, out EditorStartupStatus status)
    {
        status = default!;
        try
        {
            if (!File.Exists(path)) return false;
            status = JsonSerializer.Deserialize<EditorStartupStatus>(File.ReadAllText(path), JsonOptions)!;
            return status is not null && !string.IsNullOrWhiteSpace(status.State);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
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
        DateTimeOffset TimestampUtc);
}
