namespace BEngine.Tooling;

/// <summary>Writable locations for the Hub and Editor, never the exported Player.</summary>
public static class ToolEnvironment
{
    public const string HubDirectoryVariable = "BENGINE_HUB_DIRECTORY";
    public const string TempDirectoryVariable = "BENGINE_TOOL_TEMP_ROOT";
    public const string ProjectDirectoryVariable = "BENGINE_TOOL_PROJECT_ROOT";
    private static string? _installationDirectory;

    public static string InstallationDirectory => _installationDirectory ?? ResolveInstallationDirectory();
    public static string TempDirectory => Path.Combine(InstallationDirectory, "Temp");
    public static string CacheDirectory => Path.Combine(InstallationDirectory, "Cache");
    public static string? ProjectDirectory
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(ProjectDirectoryVariable);
            return !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value)
                ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(value)) : null;
        }
    }
    public static string SystemTemporaryDirectory => Path.Combine(ProjectDirectory ?? InstallationDirectory, "Temp", "System");

    public static string ConfigureHub()
    {
        // A second Hub installation must own its own files even when it was
        // launched from another Hub. Updater copies instead inherit that owner.
        var processPath = Environment.ProcessPath;
        var directory = string.Equals(Path.GetFileName(processPath), "BEngine.Hub.exe",
            StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(Path.GetFullPath(processPath!))!
            : ResolveInstallationDirectory();
        return Configure(directory);
    }

    public static string ConfigureEditor() => Configure(ResolveInstallationDirectory());

    public static string Configure(string installationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationDirectory);
        _installationDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationDirectory));
        Environment.SetEnvironmentVariable(ProjectDirectoryVariable, null, EnvironmentVariableTarget.Process);
        RemoveLegacyToolOverrides(null);
        MigrateLegacySharedCaches();
        foreach (var (name, value) in CreateVariables())
            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
        return _installationDirectory;
    }

    /// <summary>Scopes subsequent temporary files and tool children to an opened project.</summary>
    public static string ConfigureProject(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var project = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        var temporary = Path.Combine(project, "Temp", "System");
        // Do not leave process state pointing at a project which could not open.
        Directory.CreateDirectory(temporary);
        Environment.SetEnvironmentVariable(ProjectDirectoryVariable, project, EnvironmentVariableTarget.Process);
        foreach (var name in new[] { "TEMP", "TMP", "TMPDIR" })
            Environment.SetEnvironmentVariable(name, temporary, EnvironmentVariableTarget.Process);
        return project;
    }

    public static void ApplyTo(IDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        RemoveLegacyToolOverrides(environment);
        foreach (var (name, value) in CreateVariables()) environment[name] = value;
        if (ProjectDirectory is { } project) environment[ProjectDirectoryVariable] = project;
        else environment.Remove(ProjectDirectoryVariable);
    }

    /// <summary>Uses native NuGet configuration and caches while retaining tool/project temp paths.</summary>
    public static void ApplyToDotNetBuild(IDictionary<string, string?> environment) => ApplyTo(environment);

    private static Dictionary<string, string> CreateVariables()
    {
        var root = InstallationDirectory;
        var temp = Path.Combine(root, "Temp");
        var cache = Path.Combine(root, "Cache");
        var system = SystemTemporaryDirectory;
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [HubDirectoryVariable] = root,
            [TempDirectoryVariable] = temp,
            ["TEMP"] = system,
            ["TMP"] = system,
            ["TMPDIR"] = system,
            ["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(cache, "DotNet", "Bundles"),
            ["XDG_CACHE_HOME"] = Path.Combine(cache, "Tools", "Cache"),
            ["XDG_CONFIG_HOME"] = Path.Combine(root, "Preferences", "Tools", "Config"),
            ["XDG_DATA_HOME"] = Path.Combine(root, "Preferences", "Tools", "Data"),
            ["CODEX_HOME"] = Path.Combine(root, "Preferences", "Codex"),
            ["ANDROID_USER_HOME"] = Path.Combine(root, "Preferences", "Tools", "Android"),
            ["ANDROID_EMULATOR_HOME"] = Path.Combine(cache, "Android", "Emulator"),
            ["ANDROID_AVD_HOME"] = Path.Combine(root, "Preferences", "Tools", "Android", "Avd"),
            ["GRADLE_USER_HOME"] = Path.Combine(cache, "Gradle"),
            ["EM_CACHE"] = Path.Combine(cache, "Emscripten", "Cache"),
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false"
        };
        // Only the active temporary directory must already exist for APIs such
        // as Path.GetTempFileName. Tools create their own caches on first use.
        Directory.CreateDirectory(system);
        return variables;
    }

    private static void RemoveLegacyToolOverrides(IDictionary<string, string?>? environment)
    {
        foreach (var (name, relative) in new[]
        {
            ("NUGET_PACKAGES", "Temp/NuGet/Packages"),
            ("NUGET_HTTP_CACHE_PATH", "Temp/NuGet/Http"),
            ("NUGET_SCRATCH", "Temp/NuGet/Scratch"),
            ("NUGET_PLUGINS_CACHE_PATH", "Temp/NuGet/Plugins"),
            ("DOTNET_CLI_HOME", "Temp/DotNet/Home"),
            ("DOTNET_CLI_HOME", "Cache/DotNet/Home"),
            ("APPDATA", "Preferences/Tools/Roaming"),
            ("LOCALAPPDATA", "Temp/Tools/Local")
        })
        {
            var value = environment is null ? Environment.GetEnvironmentVariable(name)
                : environment.TryGetValue(name, out var current) ? current : null;
            if (!IsSamePath(value, Path.Combine(InstallationDirectory, relative))) continue;
            var replacement = name switch
            {
                "APPDATA" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LOCALAPPDATA" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                _ => null
            };
            if (environment is null) Environment.SetEnvironmentVariable(name, replacement, EnvironmentVariableTarget.Process);
            else if (string.IsNullOrEmpty(replacement)) environment.Remove(name);
            else environment[name] = replacement;
        }
    }

    private static bool IsSamePath(string? value, string expected)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)) return false;
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(value)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        { return false; }
    }

    private static void MigrateLegacySharedCaches()
    {
        foreach (var name in new[] { "DotNet", "Tools", "Android", "Gradle", "Emscripten" })
            MoveAvailableCache(Path.Combine(TempDirectory, name), Path.Combine(CacheDirectory, name));
    }

    private static void MoveAvailableCache(string source, string target)
    {
        if (!Directory.Exists(source)) return;
        try
        {
            // Never traverse a link or copy a cache in use. A rename either
            // succeeds, or the old data stays available for a later start.
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) return;
            if (!HasCacheContent(source)) return;
            if (!Directory.Exists(target))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Directory.Move(source, target);
                return;
            }
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) return;
            foreach (var child in Directory.EnumerateFileSystemEntries(source))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                var destination = Path.Combine(target, Path.GetFileName(child));
                if (Directory.Exists(child)) MoveAvailableCache(child, destination);
                else if (!File.Exists(destination) && !Directory.Exists(destination)) File.Move(child, destination);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { /* Preserve old data when a tool or another Hub still owns it. */ }
    }

    private static bool HasCacheContent(string directory)
    {
        foreach (var child in Directory.EnumerateFileSystemEntries(directory))
        {
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
            if (!Directory.Exists(child) || HasCacheContent(child)) return true;
        }
        return false;
    }

    private static string ResolveInstallationDirectory()
    {
        var inherited = Environment.GetEnvironmentVariable(HubDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(inherited) && Path.IsPathFullyQualified(inherited))
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(inherited));

        var processDirectory = Environment.ProcessPath is { Length: > 0 } processPath
            ? Path.GetDirectoryName(Path.GetFullPath(processPath)) : null;
        foreach (var start in new[] { processDirectory, AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(start)) continue;
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "BEngine.Hub.exe")))
                    return directory.FullName;
                if (File.Exists(Path.Combine(directory.FullName, "Engine", "src", "BEngine.sln")))
                    return Path.Combine(directory.FullName, "Engine", "Engine");
                if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
                    return Path.Combine(directory.FullName, "Engine");
                if (directory.Name.Equals("BEgine", StringComparison.OrdinalIgnoreCase) && directory.Parent is not null)
                    return directory.Parent.FullName;
            }
        }
        return Path.GetFullPath(processDirectory ?? AppContext.BaseDirectory);
    }
}
