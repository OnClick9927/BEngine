using System.ComponentModel;
using System.Diagnostics;

namespace BEngine.Editor;

internal static class ExternalCodeEditor
{
    private static readonly Lazy<string?> CodeCommand = new(FindCodeCommand);

    public static bool Open(string filePath, int lineNumber, int columnNumber)
    {
        var configured = EditorPreferences.current.ExternalScriptEditor?.Trim().Trim('"') ?? string.Empty;
        if (configured.Length > 0 && TryStart(configured, filePath, lineNumber, columnNumber)) return true;
        if (CodeCommand.Value is { } code && TryStart(code, filePath, lineNumber, columnNumber)) return true;
        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryStart(string editor, string filePath, int lineNumber, int columnNumber)
    {
        try
        {
            var info = new ProcessStartInfo(editor) { UseShellExecute = true };
            var name = Path.GetFileNameWithoutExtension(editor);
            if (name.Contains("code", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("codium", StringComparison.OrdinalIgnoreCase))
            {
                info.ArgumentList.Add("--goto");
                info.ArgumentList.Add(lineNumber > 0
                    ? $"{filePath}:{lineNumber}:{Math.Max(1, columnNumber)}"
                    : filePath);
            }
            else if (name.Contains("rider", StringComparison.OrdinalIgnoreCase))
            {
                if (lineNumber > 0)
                {
                    info.ArgumentList.Add("--line"); info.ArgumentList.Add(lineNumber.ToString());
                    info.ArgumentList.Add("--column"); info.ArgumentList.Add(Math.Max(1, columnNumber).ToString());
                }
                info.ArgumentList.Add(filePath);
            }
            else if (name.Equals("devenv", StringComparison.OrdinalIgnoreCase))
            {
                info.ArgumentList.Add("/Edit"); info.ArgumentList.Add(filePath);
                if (lineNumber > 0)
                {
                    info.ArgumentList.Add("/Command"); info.ArgumentList.Add($"Edit.Goto {lineNumber}");
                }
            }
            else info.ArgumentList.Add(filePath);
            Process.Start(info);
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static string? FindCodeCommand()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "code.cmd", "code.exe" } : new[] { "code" };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory.Trim('"'), name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
