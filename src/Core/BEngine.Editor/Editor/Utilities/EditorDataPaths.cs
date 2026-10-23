using BEngine.Platform;

namespace BEngine.Editor;

public static class EditorDataPaths
{
    private const string LegacyDataFolderName = "EditorData";
    private static readonly Lazy<string> Root = new(Initialize, LazyThreadSafetyMode.ExecutionAndPublication);
    private static FileUtility FileSystem => EditorServiceAccess.GetRequiredService<FileUtility>();
    private static PathUtility Paths => EditorServiceAccess.GetRequiredService<PathUtility>();
    private static PlatformUtility Platform => EditorServiceAccess.GetRequiredService<PlatformUtility>();

    public static string rootPath => Root.Value;
    public static string tempPath => EnsureDirectory(ResolveTempPath(rootPath));
    public static string logsPath => EnsureDirectory(Paths.Combine(tempPath, "Logs"));
    public static string preferencesDirectoryPath => EnsureDirectory(EnginePreferencesDirectory(rootPath, EngineVersion.Current));
    public static string hubPreferencesDirectoryPath => EnsureDirectory(Paths.Combine(rootPath, "Preferences", "Hub"));
    public static string themesPath => EnsureDirectory(Paths.Combine(preferencesDirectoryPath, "Themes"));
    public static string preferencesPath => Paths.Combine(preferencesDirectoryPath, "Preferences.yaml");
    public static string editorPrefsPath => Paths.Combine(preferencesDirectoryPath, "EditorPrefs.yaml");
    public static string launcherSettingsPath => Paths.Combine(hubPreferencesDirectoryPath, "LauncherSettings.yaml");
    public static string editorBootstrapLogPath => Paths.Combine(logsPath, "EditorBootstrap.log");

    public static string GetProjectEditorDirectory(string projectRoot) =>
        EnsureDirectory(Paths.Combine(NormalizeProjectRoot(projectRoot), "Library", "Editor"));

    public static string GetProjectLogsPath(string projectRoot) =>
        EnsureDirectory(Paths.Combine(GetProjectEditorDirectory(projectRoot), "Logs"));

    public static string GetProjectLogPath(string projectRoot, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.IndexOfAny(['/', '\\']) >= 0 ||
            !fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The project log file name must be a simple .log file name.", nameof(fileName));
        return Paths.Combine(GetProjectLogsPath(projectRoot), fileName);
    }

    public static string GetStartupStatusPath(string projectRoot, string token)
    {
        if (!Guid.TryParseExact(token, "N", out var id))
            throw new ArgumentException("The editor startup token must be a 32-character GUID.", nameof(token));
        return Paths.Combine(
            EnsureDirectory(Paths.Combine(GetProjectEditorDirectory(projectRoot), "Startup")),
            $"{id:N}.yaml");
    }

    public static string GetProjectLayoutsPath(string projectRoot) =>
        EnsureDirectory(Paths.Combine(GetProjectEditorDirectory(projectRoot), "Layouts"));

    public static string GetProjectEditorPrefsPath(string projectRoot) =>
        Paths.Combine(EnsureDirectory(Paths.Combine(GetProjectEditorDirectory(projectRoot), "Preferences")),
            "EditorPrefs.yaml");

    private static string Initialize()
    {
        var root = ResolveRootPath();
        MigrateTempData(root);
        MigrateUnversionedPreferences(root, EngineVersion.Current);
        MigrateExportData(root);
        MigrateRootPreferences(root);
        MigrateLegacyData(root);
        return root;
    }

    private static string EnginePreferencesDirectory(string root, string version) =>
        Paths.Combine(root, "Preferences", $"BEgine-{version}");

    private static string ResolveTempPath(string root)
    {
        return Paths.Combine(root, "Temp");
    }

    private static void MigrateTempData(string root)
    {
        var sourceRoot = Paths.Combine(root, "Temp");
        var destinationRoot = ResolveTempPath(root);
        if (PathsEqual(sourceRoot, destinationRoot) || !FileSystem.DirectoryExists(sourceRoot)) return;

        foreach (var directory in FileSystem.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            FileSystem.CreateDirectory(Paths.Combine(destinationRoot, Paths.GetRelativePath(sourceRoot, directory)));
        foreach (var source in FileSystem.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).ToArray())
        {
            try
            {
                MovePreservingExisting(source, Paths.Combine(destinationRoot, Paths.GetRelativePath(sourceRoot, source)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Leave files held by an older process in place and retry next launch.
            }
        }
        try { DeleteEmptyDirectories(sourceRoot); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static string PreferenceDestination(string root, string relative, string version) =>
        relative.Equals("LauncherSettings.yaml", StringComparison.OrdinalIgnoreCase)
            ? Paths.Combine(root, "Preferences", "Hub", relative)
            : Paths.Combine(EnginePreferencesDirectory(root, version), relative);

    private static void MigrateUnversionedPreferences(string root, string version)
    {
        var preferencesRoot = Paths.Combine(root, "Preferences");
        if (!FileSystem.DirectoryExists(preferencesRoot)) return;

        // Snapshot only the old, unscoped entries. Never import another engine version's settings.
        var sources = FileSystem.EnumerateFiles(preferencesRoot, "*", SearchOption.TopDirectoryOnly).ToList();
        var legacyDirectories = FileSystem.EnumerateDirectories(preferencesRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(directory => !Paths.GetFileName(directory).Equals("Hub", StringComparison.OrdinalIgnoreCase) &&
                                !Paths.GetFileName(directory).Equals("Codex", StringComparison.OrdinalIgnoreCase) &&
                                !Paths.GetFileName(directory).Equals("Tools", StringComparison.OrdinalIgnoreCase) &&
                                !Paths.GetFileName(directory).StartsWith("BEgine-", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var directory in legacyDirectories)
            sources.AddRange(FileSystem.EnumerateFiles(directory, "*", SearchOption.AllDirectories));

        foreach (var source in sources)
        {
            try
            {
                MovePreservingExisting(source,
                    PreferenceDestination(root, Paths.GetRelativePath(preferencesRoot, source), version));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Leave locked files in place and retry on the next launch.
            }
        }
        foreach (var directory in legacyDirectories)
        {
            try { DeleteEmptyDirectories(directory); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string ResolveRootPath()
    {
        var installation = Platform.GetEnvironmentVariable(BEngine.Tooling.ToolEnvironment.HubDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(installation)) return Paths.GetFullPath(installation);
        var configured = Platform.GetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return Paths.GetFullPath(configured);

        var executableDirectory = Platform.ProcessPath is { Length: > 0 } processPath
            ? Paths.GetDirectoryName(Paths.GetFullPath(processPath))
            : null;
        // A standalone Hub owns its adjacent preferences before any engine or
        // package directories exist. Do not borrow a parent installation or the
        // single-file bundle's extraction directory in that bootstrap state.
        if (executableDirectory is not null &&
            Paths.GetFileName(Platform.ProcessPath!).Equals("BEngine.Hub.exe", StringComparison.OrdinalIgnoreCase))
            return executableDirectory;

        foreach (var start in new[] { executableDirectory, Platform.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(start)) continue;
            for (string? directory = Paths.TrimEndingDirectorySeparator(Paths.GetFullPath(start));
                 directory is not null;
                 directory = ParentDirectory(directory))
            {
                var parent = ParentDirectory(directory);
                if (FileSystem.DirectoryExists(Paths.Combine(directory, "BEgine")) &&
                    FileSystem.DirectoryExists(Paths.Combine(directory, "Packages")))
                    return directory;
                if (parent is not null &&
                    (Paths.GetFileName(directory).Equals("BEgine", StringComparison.OrdinalIgnoreCase) ||
                     (Paths.GetFileName(directory).Equals("Hub", StringComparison.OrdinalIgnoreCase) &&
                      FileSystem.DirectoryExists(Paths.Combine(parent, "BEgine")))))
                    return parent;

                if (FileSystem.FileExists(Paths.Combine(directory, "Engine", "src", "BEngine.sln")))
                    return Paths.Combine(directory, "Engine", "Engine");
                if (FileSystem.FileExists(Paths.Combine(directory, "src", "BEngine.sln")))
                    return Paths.Combine(directory, "Engine");
            }
        }

        return Paths.GetFullPath(Platform.BaseDirectory);
    }

    private static void MigrateExportData(string root)
    {
        var legacyRoot = Paths.Combine(root, LegacyDataFolderName);
        if (!FileSystem.DirectoryExists(legacyRoot)) return;

        foreach (var source in FileSystem.EnumerateFiles(legacyRoot, "*", SearchOption.AllDirectories).ToArray())
        {
            var relative = Paths.GetRelativePath(legacyRoot, source);
            var parts = relative.Split([Paths.DirectorySeparatorChar, Paths.AltDirectorySeparatorChar], 2);
            var destination = parts[0] switch
            {
                "Preferences" when parts.Length == 2 => PreferenceDestination(root, parts[1], EngineVersion.Current),
                "CommandLine" when parts.Length == 2 => Paths.Combine(ResolveTempPath(root), parts[1]),
                "Logs" => Paths.Combine(ResolveTempPath(root), relative),
                "Preferences.yaml" or "EditorPrefs.yaml" or "LauncherSettings.yaml" or "CodexSettings.yaml"
                    => PreferenceDestination(root, relative, EngineVersion.Current),
                _ => Paths.Combine(ResolveTempPath(root), "LegacyEditorData", relative)
            };
            try { MovePreservingExisting(source, destination); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Preserve files still held by an older process and retry on the next launch.
            }
        }

        try { DeleteEmptyDirectories(legacyRoot); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void MigrateLegacyData(string destinationRoot)
    {
        var legacyRoot = Paths.Combine(
            Platform.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BEngine");
        if (!FileSystem.DirectoryExists(legacyRoot) || PathsEqual(legacyRoot, destinationRoot)) return;

        var knownFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Preferences.yaml", "EditorPrefs.yaml", "LauncherSettings.yaml", "CodexSettings.yaml" };

        foreach (var source in FileSystem.EnumerateFiles(legacyRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Paths.GetRelativePath(legacyRoot, source);
            // Import only known global settings. Project logs and transient files
            // cannot be assigned to a project from their legacy location alone.
            if (!knownFiles.Contains(relative)) continue;
            try
            {
                MovePreservingExisting(source, PreferenceDestination(destinationRoot, relative, EngineVersion.Current));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A previous editor instance may still own a log. Retry on the next launch.
            }
        }

        try { DeleteEmptyDirectories(legacyRoot); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void MigrateRootPreferences(string root)
    {
        foreach (var fileName in new[] { "Preferences.yaml", "EditorPrefs.yaml", "LauncherSettings.yaml", "CodexSettings.yaml" })
        {
            var source = Paths.Combine(root, fileName);
            if (!FileSystem.FileExists(source)) continue;
            try
            {
                MovePreservingExisting(source, PreferenceDestination(root, fileName, EngineVersion.Current));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A running editor can briefly own the file. Retry on the next launch.
            }
        }
    }

    private static void MovePreservingExisting(string source, string destination)
    {
        FileSystem.CreateDirectory(Paths.GetDirectoryName(destination)!);
        if (FileSystem.FileExists(destination))
        {
            var backupDirectory = Paths.Combine(Paths.GetDirectoryName(destination)!, "MigratedLegacy");
            FileSystem.CreateDirectory(backupDirectory);
            destination = Paths.Combine(backupDirectory,
                $"{Paths.GetFileNameWithoutExtension(destination)}-{Platform.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}" +
                Paths.GetExtension(destination));
        }

        FileSystem.CopyFile(source, destination, overwrite: false);
        FileSystem.DeleteFile(source);
    }

    private static void DeleteEmptyDirectories(string root)
    {
        foreach (var directory in FileSystem.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
            if (!FileSystem.EnumerateFileSystemEntries(directory).Any())
                FileSystem.DeleteDirectory(directory);
        if (!FileSystem.EnumerateFileSystemEntries(root).Any()) FileSystem.DeleteDirectory(root);
    }

    private static string EnsureDirectory(string path)
    {
        FileSystem.CreateDirectory(path);
        return path;
    }

    private static string NormalizeProjectRoot(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var fullPath = Paths.GetFullPath(projectRoot);
        return FileSystem.FileExists(fullPath) ||
               Paths.GetFileName(fullPath).Equals(ProjectSystem.ProjectWorkspace.ProjectFileName,
                   StringComparison.OrdinalIgnoreCase)
            ? Paths.GetDirectoryName(fullPath) ??
              throw new InvalidDataException("Project path has no directory.")
            : fullPath;
    }

    private static bool PathsEqual(string left, string right) =>
        Paths.GetFullPath(left).TrimEnd(Paths.DirectorySeparatorChar)
            .Equals(Paths.GetFullPath(right).TrimEnd(Paths.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

    private static string? ParentDirectory(string path)
    {
        var normalized = Paths.TrimEndingDirectorySeparator(Paths.GetFullPath(path));
        var parent = Paths.GetDirectoryName(normalized);
        return string.IsNullOrWhiteSpace(parent) ||
               parent.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            ? null
            : parent;
    }
}
