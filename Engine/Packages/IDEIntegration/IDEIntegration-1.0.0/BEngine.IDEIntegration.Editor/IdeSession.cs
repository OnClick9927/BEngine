using System.Diagnostics;
using System.Text.Json;
using BEngine.ProjectSystem;

namespace BEngine.IDE.Editor;

/// <summary>Project-local rendezvous for IDE clients. No listening socket, heartbeat thread or global user cache.</summary>
public sealed class IdeSession : IDisposable
{
    private readonly ProjectWorkspace _workspace;
    private readonly string _directory;
    private readonly string _targetPath;
    private bool _disposed;

    public IdeSession(ProjectWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _directory = Path.Combine(workspace.LibraryPath, "IDE");
        _targetPath = Path.Combine(_directory, "Targets", Environment.ProcessId + ".json");
        Synchronize();
    }

    public string Synchronize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var solution = IdeSolutionGenerator.Generate(_workspace);
        using var process = Process.GetCurrentProcess();
        IdeSolutionGenerator.AtomicWrite(_targetPath, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1, ProcessId = Environment.ProcessId,
            ProcessStartTimeUtc = process.StartTime.ToUniversalTime().ToString("O"),
            ProjectPath = _workspace.RootPath, ProjectName = _workspace.Project.Name,
            Kind = "editor", DisplayName = "BEngine Editor — " + _workspace.Project.Name,
            ExecutablePath = Environment.ProcessPath ?? string.Empty, SolutionPath = solution, Runtime = "coreclr"
        }, IdeSolutionGenerator.Json));
        return solution;
    }

    /// <summary>Run at most twice per second on the editor owner thread. Each pass handles at most four messages.</summary>
    public void PollRequests()
    {
        if (_disposed) return;
        var requests = Path.Combine(_directory, "Requests");
        if (!Directory.Exists(requests)) return;
        var handled = 0;
        foreach (var path in Directory.EnumerateFiles(requests, "*.json", SearchOption.TopDirectoryOnly))
        {
            if (++handled > 4) break;
            var id = Path.GetFileNameWithoutExtension(path);
            if (!Guid.TryParseExact(id, "N", out _) && !Guid.TryParseExact(id, "D", out _)) continue;
            string? solution = null;
            string? error = null;
            try
            {
                if (new FileInfo(path).Length > 65536) throw new InvalidDataException("IDE request exceeds 64 KiB.");
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var request = document.RootElement;
                if (request.GetProperty("schemaVersion").GetInt32() != 1 ||
                    request.GetProperty("requestId").GetString() != id ||
                    request.GetProperty("command").GetString() != "sync")
                    throw new InvalidDataException("Unsupported IDE request.");
                solution = Synchronize();
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
                JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            { error = exception.Message; }
            IdeSolutionGenerator.AtomicWrite(Path.Combine(_directory, "Responses", id + ".json"),
                JsonSerializer.Serialize(new { SchemaVersion = 1, RequestId = id, Success = error is null,
                    SolutionPath = solution, Error = error }, IdeSolutionGenerator.Json));
            File.Delete(path);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { File.Delete(_targetPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { BEngine.Debug.LogWarning("IDE target registration could not be removed: " + exception.Message); }
    }
}
