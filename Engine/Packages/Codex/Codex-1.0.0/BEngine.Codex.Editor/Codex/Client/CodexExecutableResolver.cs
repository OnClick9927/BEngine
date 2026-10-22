namespace BEngine.Editor.Codex;

internal static class CodexExecutableResolver
{
    private static readonly string[] WindowsExecutableNames =
        ["codex.exe", "codex.com", "codex.cmd", "codex.bat", "codex"];

    internal static string Resolve(string? configuredPath, string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        if (TryResolveConfigured(configuredPath, projectRoot, out var executable)) return executable;

        var environmentPath = EditorHostServices.Platform.GetEnvironmentVariable("BENGINE_CODEX_PATH");
        if (TryResolveConfigured(environmentPath, projectRoot, out executable)) return executable;

        if (TryResolveFromSearchPath(out executable)) return executable;
        if (EditorHostServices.Platform.IsWindows && TryResolveDesktopInstallation(out executable)) return executable;

        throw new FileNotFoundException(
            "The Codex CLI was not found. Install Codex or set its full path in Codex Settings " +
            "(or BENGINE_CODEX_PATH).",
            "codex");
    }

    internal static string BuildEffectiveSearchPath() =>
        string.Join(EditorHostServices.Paths.PathSeparator, EnumerateSearchDirectories());

    private static bool TryResolveConfigured(string? value, string projectRoot, out string executable)
    {
        executable = string.Empty;
        var configured = Normalize(value);
        if (configured.Length == 0) return false;

        if (EditorHostServices.Paths.IsPathRooted(configured) || ContainsDirectorySeparator(configured))
        {
            var path = EditorHostServices.Paths.IsPathRooted(configured)
                ? EditorHostServices.Paths.GetFullPath(configured)
                : EditorHostServices.Paths.GetFullPath(EditorHostServices.Paths.Combine(projectRoot, configured));
            if (TryResolvePathOrDirectory(path, out executable)) return true;
            throw new FileNotFoundException("The configured Codex CLI path does not exist.", path);
        }

        if (TryResolveCommandName(configured, out executable)) return true;
        throw new FileNotFoundException($"The configured Codex command '{configured}' was not found on PATH.", configured);
    }

    private static bool TryResolveFromSearchPath(out string executable) =>
        TryResolveCommandName("codex", out executable);

    private static bool TryResolveCommandName(string commandName, out string executable)
    {
        executable = string.Empty;
        foreach (var directory in EnumerateSearchDirectories())
        {
            if (IsRestrictedPackagedApplicationDirectory(directory)) continue;
            foreach (var fileName in CandidateNames(commandName))
            {
                string candidate;
                try
                {
                    candidate = EditorHostServices.Paths.GetFullPath(EditorHostServices.Paths.Combine(directory, fileName));
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    continue;
                }
                if (!EditorHostServices.FileSystem.FileExists(candidate)) continue;
                executable = candidate;
                return true;
            }
        }
        return false;
    }

    private static bool TryResolveDesktopInstallation(out string executable)
    {
        executable = string.Empty;
        var localAppData = EditorHostServices.Platform.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (localAppData.Length == 0) return false;
        var binRoot = EditorHostServices.Paths.Combine(localAppData, "OpenAI", "Codex", "bin");
        if (!EditorHostServices.FileSystem.DirectoryExists(binRoot)) return false;

        try
        {
            var candidate = EditorHostServices.FileSystem.EnumerateDirectories(binRoot, "*", SearchOption.TopDirectoryOnly)
                .Select(directory => EditorHostServices.Paths.Combine(directory, "codex.exe"))
                .Where(EditorHostServices.FileSystem.FileExists)
                .Select(path => new
                {
                    Path = path,
                    DirectoryStamp = EditorHostServices.FileSystem.GetLastWriteTimeUtc(
                        EditorHostServices.Paths.GetDirectoryName(path)!),
                    FileStamp = EditorHostServices.FileSystem.GetLastWriteTimeUtc(path)
                })
                .OrderByDescending(file => file.DirectoryStamp)
                .ThenByDescending(file => file.FileStamp)
                .Select(file => file.Path)
                .FirstOrDefault();
            if (candidate is null) return false;
            executable = candidate;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryResolvePathOrDirectory(string path, out string executable)
    {
        executable = string.Empty;
        if (EditorHostServices.FileSystem.FileExists(path))
        {
            executable = path;
            return true;
        }
        if (!EditorHostServices.FileSystem.DirectoryExists(path)) return TryResolveWithExtensions(path, out executable);
        foreach (var fileName in CandidateNames("codex"))
        {
            var candidate = EditorHostServices.Paths.Combine(path, fileName);
            if (!EditorHostServices.FileSystem.FileExists(candidate)) continue;
            executable = EditorHostServices.Paths.GetFullPath(candidate);
            return true;
        }
        return false;
    }

    private static bool TryResolveWithExtensions(string path, out string executable)
    {
        executable = string.Empty;
        if (!EditorHostServices.Platform.IsWindows || EditorHostServices.HasExtension(path)) return false;
        foreach (var extension in new[] { ".exe", ".com", ".cmd", ".bat" })
        {
            var candidate = path + extension;
            if (!EditorHostServices.FileSystem.FileExists(candidate)) continue;
            executable = EditorHostServices.Paths.GetFullPath(candidate);
            return true;
        }
        return false;
    }

    private static IEnumerable<string> EnumerateSearchDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in SearchPathValues())
        {
            foreach (var segment in value.Split(EditorHostServices.Paths.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var directory = Normalize(segment);
                if (directory.Length == 0) continue;
                try { directory = EditorHostServices.Paths.GetFullPath(directory); }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    continue;
                }
                if (seen.Add(directory)) yield return directory;
            }
        }
    }

    private static IEnumerable<string> SearchPathValues()
    {
        yield return EditorHostServices.Platform.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (!EditorHostServices.Platform.IsWindows) yield break;

        string? userPath = null;
        string? machinePath = null;
        try
        {
            userPath = EditorHostServices.Platform.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
            machinePath = EditorHostServices.Platform.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine);
        }
        catch (System.Security.SecurityException)
        {
        }
        yield return userPath ?? string.Empty;
        yield return machinePath ?? string.Empty;
    }

    private static IEnumerable<string> CandidateNames(string commandName)
    {
        if (!EditorHostServices.Platform.IsWindows || EditorHostServices.HasExtension(commandName))
        {
            yield return commandName;
            yield break;
        }
        foreach (var name in WindowsExecutableNames)
            yield return commandName.Equals("codex", StringComparison.OrdinalIgnoreCase)
                ? name
                : commandName + EditorHostServices.Paths.GetExtension(name);
    }

    private static bool IsRestrictedPackagedApplicationDirectory(string directory) =>
        EditorHostServices.Platform.IsWindows &&
        directory.Contains($"{EditorHostServices.Paths.DirectorySeparatorChar}Program Files{EditorHostServices.Paths.DirectorySeparatorChar}WindowsApps{EditorHostServices.Paths.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

    private static bool ContainsDirectorySeparator(string value) =>
        value.Contains(EditorHostServices.Paths.DirectorySeparatorChar) || value.Contains(EditorHostServices.Paths.AltDirectorySeparatorChar);

    private static string Normalize(string? value) =>
        EditorHostServices.Platform.ExpandEnvironmentVariables(value?.Trim().Trim('"') ?? string.Empty);
}
