namespace BEngine.Editor.Codex;

internal static class CodexExecutableResolver
{
    private static readonly string[] WindowsExecutableNames =
        ["codex.exe", "codex.com", "codex.cmd", "codex.bat", "codex"];

    internal static string Resolve(string? configuredPath, string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        if (TryResolveConfigured(configuredPath, projectRoot, out var executable)) return executable;

        var environmentPath = Environment.GetEnvironmentVariable("BENGINE_CODEX_PATH");
        if (TryResolveConfigured(environmentPath, projectRoot, out executable)) return executable;

        if (TryResolveFromSearchPath(out executable)) return executable;
        if (OperatingSystem.IsWindows() && TryResolveDesktopInstallation(out executable)) return executable;

        throw new FileNotFoundException(
            "The Codex CLI was not found. Install Codex or set its full path in Codex Settings " +
            "(or BENGINE_CODEX_PATH).",
            "codex");
    }

    internal static string BuildEffectiveSearchPath() =>
        string.Join(Path.PathSeparator, EnumerateSearchDirectories());

    private static bool TryResolveConfigured(string? value, string projectRoot, out string executable)
    {
        executable = string.Empty;
        var configured = Normalize(value);
        if (configured.Length == 0) return false;

        if (Path.IsPathRooted(configured) || ContainsDirectorySeparator(configured))
        {
            var path = Path.IsPathRooted(configured)
                ? Path.GetFullPath(configured)
                : Path.GetFullPath(Path.Combine(projectRoot, configured));
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
                    candidate = Path.GetFullPath(Path.Combine(directory, fileName));
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    continue;
                }
                if (!File.Exists(candidate)) continue;
                executable = candidate;
                return true;
            }
        }
        return false;
    }

    private static bool TryResolveDesktopInstallation(out string executable)
    {
        executable = string.Empty;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (localAppData.Length == 0) return false;
        var binRoot = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
        if (!Directory.Exists(binRoot)) return false;

        try
        {
            var candidate = Directory.EnumerateDirectories(binRoot, "*", SearchOption.TopDirectoryOnly)
                .Select(directory => Path.Combine(directory, "codex.exe"))
                .Where(File.Exists)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.Directory?.LastWriteTimeUtc ?? DateTime.MinValue)
                .ThenByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
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
        if (File.Exists(path))
        {
            executable = path;
            return true;
        }
        if (!Directory.Exists(path)) return TryResolveWithExtensions(path, out executable);
        foreach (var fileName in CandidateNames("codex"))
        {
            var candidate = Path.Combine(path, fileName);
            if (!File.Exists(candidate)) continue;
            executable = Path.GetFullPath(candidate);
            return true;
        }
        return false;
    }

    private static bool TryResolveWithExtensions(string path, out string executable)
    {
        executable = string.Empty;
        if (!OperatingSystem.IsWindows() || Path.HasExtension(path)) return false;
        foreach (var extension in new[] { ".exe", ".com", ".cmd", ".bat" })
        {
            var candidate = path + extension;
            if (!File.Exists(candidate)) continue;
            executable = Path.GetFullPath(candidate);
            return true;
        }
        return false;
    }

    private static IEnumerable<string> EnumerateSearchDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in SearchPathValues())
        {
            foreach (var segment in value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var directory = Normalize(segment);
                if (directory.Length == 0) continue;
                try { directory = Path.GetFullPath(directory); }
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
        yield return Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (!OperatingSystem.IsWindows()) yield break;

        string? userPath = null;
        string? machinePath = null;
        try
        {
            userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
            machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine);
        }
        catch (System.Security.SecurityException)
        {
        }
        yield return userPath ?? string.Empty;
        yield return machinePath ?? string.Empty;
    }

    private static IEnumerable<string> CandidateNames(string commandName)
    {
        if (!OperatingSystem.IsWindows() || Path.HasExtension(commandName))
        {
            yield return commandName;
            yield break;
        }
        foreach (var name in WindowsExecutableNames)
            yield return commandName.Equals("codex", StringComparison.OrdinalIgnoreCase)
                ? name
                : commandName + Path.GetExtension(name);
    }

    private static bool IsRestrictedPackagedApplicationDirectory(string directory) =>
        OperatingSystem.IsWindows() &&
        directory.Contains($"{Path.DirectorySeparatorChar}Program Files{Path.DirectorySeparatorChar}WindowsApps{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

    private static bool ContainsDirectorySeparator(string value) =>
        value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar);

    private static string Normalize(string? value) =>
        Environment.ExpandEnvironmentVariables(value?.Trim().Trim('"') ?? string.Empty);
}
