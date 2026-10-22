using System.ComponentModel;
using BEngine.Platform;

namespace BEngine.Editor;

internal static class ExternalCodeEditor
{
    private static readonly Lazy<string?> CodeCommand = new(FindCodeCommand);

    public static bool Open(string filePath, int lineNumber, int columnNumber)
    {
        var processHost = EditorServiceAccess.GetRequiredService<ProcessHostUtility>();
        var configured = EditorPreferences.current.ExternalScriptEditor?.Trim().Trim('"') ?? string.Empty;
        if (configured.Length > 0 && TryStart(
                processHost, configured, filePath, lineNumber, columnNumber)) return true;
        if (CodeCommand.Value is { } code && TryStart(
                processHost, code, filePath, lineNumber, columnNumber)) return true;
        try
        {
            processHost.StartDetached(new ProcessHostRequest(filePath)
            {
                UseShellExecute = true,
                CreateNoWindow = false
            });
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryStart(
        ProcessHostUtility processHost,
        string editor,
        string filePath,
        int lineNumber,
        int columnNumber)
    {
        try
        {
            var request = new ProcessHostRequest(editor)
            {
                UseShellExecute = true,
                CreateNoWindow = false
            };
            var name = EditorHostServices.Paths.GetFileNameWithoutExtension(editor);
            if (name.Contains("code", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("codium", StringComparison.OrdinalIgnoreCase))
            {
                request.Arguments.Add("--goto");
                request.Arguments.Add(lineNumber > 0
                    ? $"{filePath}:{lineNumber}:{Math.Max(1, columnNumber)}"
                    : filePath);
            }
            else if (name.Contains("rider", StringComparison.OrdinalIgnoreCase))
            {
                if (lineNumber > 0)
                {
                    request.Arguments.Add("--line"); request.Arguments.Add(lineNumber.ToString());
                    request.Arguments.Add("--column");
                    request.Arguments.Add(Math.Max(1, columnNumber).ToString());
                }
                request.Arguments.Add(filePath);
            }
            else if (name.Equals("devenv", StringComparison.OrdinalIgnoreCase))
            {
                request.Arguments.Add("/Edit"); request.Arguments.Add(filePath);
                if (lineNumber > 0)
                {
                    request.Arguments.Add("/Command");
                    request.Arguments.Add($"Edit.Goto {lineNumber}");
                }
            }
            else request.Arguments.Add(filePath);
            processHost.StartDetached(request);
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static string? FindCodeCommand()
    {
        var names = EditorHostServices.Platform.IsWindows ? new[] { "code.cmd", "code.exe" } : new[] { "code" };
        foreach (var directory in (EditorHostServices.Platform.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(EditorHostServices.Paths.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                var candidate = EditorHostServices.Paths.Combine(directory.Trim('"'), name);
                if (EditorHostServices.FileSystem.FileExists(candidate)) return candidate;
            }
        }
        return null;
    }
}
