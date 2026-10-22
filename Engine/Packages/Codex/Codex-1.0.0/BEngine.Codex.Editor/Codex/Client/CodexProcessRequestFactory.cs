using BEngine.Platform;

namespace BEngine.Editor.Codex;

internal static class CodexProcessRequestFactory
{
    internal static string ResolveHome(string projectRoot)
    {
        var configured = EditorHostServices.Platform.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(configured))
            return EditorHostServices.Paths.Combine(EditorHostServices.Platform.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        return EditorHostServices.Paths.GetFullPath(EditorHostServices.Paths.IsPathRooted(configured)
            ? configured : EditorHostServices.Paths.Combine(projectRoot, configured));
    }

    internal static ProcessHostRequest Create(string executable, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var extension = EditorHostServices.Paths.GetExtension(executable);
        if (EditorHostServices.Platform.IsWindows && extension is ".cmd" or ".bat")
        {
            var commandInterpreter = EditorHostServices.Platform.GetEnvironmentVariable("ComSpec");
            if (string.IsNullOrWhiteSpace(commandInterpreter))
                commandInterpreter = EditorHostServices.Paths.Combine(
                    EditorHostServices.Platform.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            return CreateBase(commandInterpreter, workingDirectory,
                $"/d /s /c \"\"{executable}\" app-server\"");
        }

        var direct = CreateBase(executable, workingDirectory);
        direct.Arguments.Add("app-server");
        return direct;
    }

    private static ProcessHostRequest CreateBase(
        string executable,
        string workingDirectory,
        string? rawArguments = null)
    {
        var request = new ProcessHostRequest(executable)
        {
            WorkingDirectory = workingDirectory,
            RawArguments = rawArguments,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var searchPath = CodexExecutableResolver.BuildEffectiveSearchPath();
        request.EnvironmentVariables["CODEX_HOME"] = ResolveHome(workingDirectory);
        if (searchPath.Length > 0) request.EnvironmentVariables["PATH"] = searchPath;
        return request;
    }
}
