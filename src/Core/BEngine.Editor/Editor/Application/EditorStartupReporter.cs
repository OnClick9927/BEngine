using System.Diagnostics;

using BEngine.Platform;

namespace BEngine.Editor;

internal sealed class EditorStartupReporter : IDisposable
{
    internal const string TokenOption = "--startup-token";
    private readonly string _statusPath;
    private readonly FileUtility _fileSystem;
    private readonly PlatformUtility _platformService;
    private EditorProgressInfo? _lastProgress;
    private long _revision;
    private bool _reporting;

    private EditorStartupReporter(
        string statusPath,
        FileUtility fileSystem,
        PlatformUtility platformService)
    {
        _statusPath = statusPath;
        _fileSystem = fileSystem;
        _platformService = platformService;
    }

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
        if (optionIndex == 0 || arguments[0].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("A project path is required when reporting editor startup.", nameof(arguments));
        return new EditorStartupReporter(
            EditorDataPaths.GetStartupStatusPath(arguments[0], arguments[optionIndex + 1]),
            EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PlatformUtility>());
    }

    internal void ReportStarting()
    {
        Write("starting", null, null);
        if (_reporting) return;
        _reporting = true;
        EditorUtility.progressChanged += OnProgressChanged;
    }

    internal bool TryReportReady()
    {
        Dispose();
        return TryWrite("ready", null, null);
    }

    internal bool TryReportFailure(Exception exception, string? logPath)
    {
        Dispose();
        return TryWrite("failed", exception.GetBaseException().Message, logPath);
    }

    public void Dispose()
    {
        _reporting = false;
        EditorUtility.progressChanged -= OnProgressChanged;
    }

    private void OnProgressChanged(EditorProgressInfo progress)
    {
        if (!_reporting || !progress.IsVisible || _lastProgress == progress) return;
        _lastProgress = progress;
        TryWrite("starting", $"{progress.Title}: {progress.Info}", null);
    }

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
        var payload = YamlUtility.Serialize(new StartupStatus(
            state, message, logPath, _platformService.ProcessId, _platformService.UtcNow, ++_revision));
        var temporaryPath = $"{_statusPath}.{_platformService.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            _fileSystem.WriteAllText(temporaryPath, payload);
            _fileSystem.MoveFile(temporaryPath, _statusPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (_fileSystem.FileExists(temporaryPath)) _fileSystem.DeleteFile(temporaryPath);
            }
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
        DateTimeOffset TimestampUtc,
        long Revision);
}
