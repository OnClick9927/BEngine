using System.Diagnostics;

namespace BEngine.Editor.Codex;

internal static class CodexProcessStartInfoFactory
{
    internal static ProcessStartInfo Create(string executable, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var extension = Path.GetExtension(executable);
        if (OperatingSystem.IsWindows() && extension is ".cmd" or ".bat")
        {
            var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec");
            if (string.IsNullOrWhiteSpace(commandInterpreter))
                commandInterpreter = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            var startInfo = CreateBase(commandInterpreter, workingDirectory);
            startInfo.Arguments = $"/d /s /c \"\"{executable}\" app-server\"";
            return startInfo;
        }

        var direct = CreateBase(executable, workingDirectory);
        direct.ArgumentList.Add("app-server");
        return direct;
    }

    private static ProcessStartInfo CreateBase(string executable, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var searchPath = CodexExecutableResolver.BuildEffectiveSearchPath();
        if (searchPath.Length > 0) startInfo.Environment["PATH"] = searchPath;
        return startInfo;
    }
}
