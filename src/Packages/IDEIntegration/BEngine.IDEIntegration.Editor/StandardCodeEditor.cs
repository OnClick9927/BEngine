using EditorCapabilities = BEngine.Editor.EditorServices;
using System.ComponentModel;
using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.IDE.Editor;

internal sealed class StandardCodeEditor : ICodeEditorIntegration
{
    private static readonly Lazy<string?> CodeCommand = new(FindCodeCommand);
    public bool Open(CodeEditorOpenRequest request)
    {
        var processHost = EditorServices.GetRequiredService<ProcessHostUtility>();
        var configured = EditorPreferences.current.ExternalScriptEditor?.Trim().Trim('"') ?? string.Empty;
        if (configured.Length > 0 && TryStart(processHost, configured, request.FilePath, request.Line, request.Column)) return true;
        return CodeCommand.Value is { } code && TryStart(processHost, code, request.FilePath, request.Line, request.Column);
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
            var name = EditorCapabilities.GetRequiredService<PathUtility>().GetFileNameWithoutExtension(editor);
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
        var names = EditorCapabilities.GetRequiredService<PlatformUtility>().IsWindows ? new[] { "code.cmd", "code.exe" } : new[] { "code" };
        foreach (var directory in (EditorCapabilities.GetRequiredService<PlatformUtility>().GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(EditorCapabilities.GetRequiredService<PathUtility>().PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                var candidate = EditorCapabilities.GetRequiredService<PathUtility>().Combine(directory.Trim('"'), name);
                if (EditorCapabilities.GetRequiredService<FileUtility>().FileExists(candidate)) return candidate;
            }
        }
        return null;
    }
}
