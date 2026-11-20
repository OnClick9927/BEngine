using System.Diagnostics;
using System.Text.Json;
using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.Workbench;

/// <summary>The default workbench's startup labels and profile file policy.</summary>
internal sealed class EditorStartupProfile : IDisposable
{
    private static EditorStartupProfile? _current;
    private readonly EditorStartupTrace _trace;
    private bool _written;

    private EditorStartupProfile(EditorStartupTrace trace) => _trace = trace;

    internal static EditorStartupProfile Start()
    {
        var profile = new EditorStartupProfile(EditorStartupTrace.Begin("Tool environment and platform"));
        _current = profile;
        return profile;
    }

    internal static void Mark(string stage) => _current?._trace.Mark(stage);

    internal void Complete(string projectPath)
    {
        if (_written) return;
        var snapshot = _trace.Complete();
        _written = true;
        if (ReferenceEquals(_current, this)) _current = null;
        try
        {
            var platform = EditorServices.GetRequiredService<PlatformUtility>();
            var paths = EditorServices.GetRequiredService<PathUtility>();
            var files = EditorServices.GetRequiredService<FileUtility>();
            var path = paths.Combine(
                EditorInstanceContext.current?.logsPath ??
                paths.Combine(projectPath, "Library", "Editor", "Logs"),
                "EditorStartupProfile.json");
            files.CreateDirectory(paths.GetDirectoryName(path)!);
            files.WriteAllText(path, JsonSerializer.Serialize(new
            {
                ProcessId = platform.ProcessId,
                ProcessToFirstFrameMilliseconds = (platform.UtcNow - platform.ProcessStartTimeUtc).TotalMilliseconds,
                snapshot.ManagedStartupMilliseconds,
                snapshot.Stages,
                snapshot.Details
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) { Trace.WriteLine($"Editor startup timing could not be saved: {error}"); }
    }

    public void Dispose()
    {
        _trace.Dispose();
        if (ReferenceEquals(_current, this)) _current = null;
    }
}
