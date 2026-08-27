using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace BEngine.Editor;

internal sealed class EditorStartupReporter
{
    internal const string TokenOption = "--startup-token";
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly string _statusPath;

    private EditorStartupReporter(string statusPath) => _statusPath = statusPath;

    internal static EditorStartupReporter? Create(IReadOnlyList<string> arguments)
    {
        var optionIndex = -1;
        for (var index = 0; index < arguments.Count; index++)
            if (arguments[index].Equals(TokenOption, StringComparison.OrdinalIgnoreCase))
            {
                optionIndex = index;
                break;
            }
        if (optionIndex < 0) return null;
        if (optionIndex + 1 >= arguments.Count)
            throw new ArgumentException($"The {TokenOption} option requires a token.", nameof(arguments));
        return new EditorStartupReporter(EditorDataPaths.GetStartupStatusPath(arguments[optionIndex + 1]));
    }

    internal void ReportStarting() => Write("starting", null, null);

    internal bool TryReportReady() => TryWrite("ready", null, null);

    internal bool TryReportFailure(Exception exception, string? logPath) =>
        TryWrite("failed", exception.GetBaseException().Message, logPath);

    private bool TryWrite(string state, string? message, string? logPath)
    {
        try
        {
            Write(state, message, logPath);
            return true;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"Editor startup status could not be written: {exception}");
            return false;
        }
    }

    private void Write(string state, string? message, string? logPath)
    {
        var payload = JsonSerializer.Serialize(new StartupStatus(
            state, message, logPath, Environment.ProcessId, DateTimeOffset.UtcNow));
        var temporaryPath = $"{_statusPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, payload, Utf8WithoutBom);
            File.Move(temporaryPath, _statusPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (Exception exception)
            {
                Trace.WriteLine($"Temporary editor startup status could not be removed: {exception}");
            }
        }
    }

    private sealed record StartupStatus(
        string State,
        string? Message,
        string? LogPath,
        int ProcessId,
        DateTimeOffset TimestampUtc);
}
