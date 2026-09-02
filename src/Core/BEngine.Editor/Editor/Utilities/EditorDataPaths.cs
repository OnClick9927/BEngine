namespace BEngine.Editor;

public static class EditorDataPaths
{
    private const string DataFolderName = "EditorData";
    private static readonly Lazy<string> Root = new(Initialize, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string rootPath => Root.Value;
    public static string logsPath => EnsureDirectory(Path.Combine(rootPath, "Logs"));
    public static string startupStatusDirectoryPath => EnsureDirectory(Path.Combine(rootPath, "Startup"));
    public static string preferencesDirectoryPath => EnsureDirectory(Path.Combine(rootPath, "Preferences"));
    public static string layoutsDirectoryPath => EnsureDirectory(Path.Combine(preferencesDirectoryPath, "Layouts"));
    public static string themesPath => EnsureDirectory(Path.Combine(preferencesDirectoryPath, "Themes"));
    public static string preferencesPath => Path.Combine(preferencesDirectoryPath, "Preferences.yaml");
    public static string editorPrefsPath => Path.Combine(preferencesDirectoryPath, "EditorPrefs.yaml");
    public static string launcherSettingsPath => Path.Combine(preferencesDirectoryPath, "LauncherSettings.yaml");
    public static string codexSettingsPath => Path.Combine(preferencesDirectoryPath, "CodexSettings.yaml");
    public static string editorBootstrapLogPath => Path.Combine(logsPath, "EditorBootstrap.log");
    public static string launcherBuildLogPath => Path.Combine(logsPath, "LauncherBuild.log");

    public static string GetStartupStatusPath(string token)
    {
        if (!Guid.TryParseExact(token, "N", out var id))
            throw new ArgumentException("The editor startup token must be a 32-character GUID.", nameof(token));
        return Path.Combine(startupStatusDirectoryPath, $"{id:N}.json");
    }

    private static string Initialize()
    {
        var root = ResolveRootPath();
        Directory.CreateDirectory(root);
        MigrateRootPreferences(root);
        MigrateLegacyData(root);
        Directory.CreateDirectory(Path.Combine(root, "Preferences", "Themes"));
        Directory.CreateDirectory(Path.Combine(root, "Preferences", "Layouts"));
        return root;
    }

    private static string ResolveRootPath()
    {
        var configured = Environment.GetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
        {
            if (directory.Name.Equals("BEgine", StringComparison.OrdinalIgnoreCase) &&
                directory.Parent is { } outputDirectory)
                return Path.Combine(outputDirectory.FullName, DataFolderName);

            if (Directory.Exists(Path.Combine(directory.FullName, "src")) &&
                Directory.Exists(Path.Combine(directory.FullName, "Output")))
                return Path.Combine(directory.FullName, "Output", DataFolderName);
        }

        return Path.Combine(AppContext.BaseDirectory, DataFolderName);
    }

    private static void MigrateLegacyData(string destinationRoot)
    {
        var legacyRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BEngine");
        if (!Directory.Exists(legacyRoot) || PathsEqual(legacyRoot, destinationRoot)) return;

        var knownFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Preferences.yaml"] = Path.Combine("Preferences", "Preferences.yaml"),
            ["EditorPrefs.yaml"] = Path.Combine("Preferences", "EditorPrefs.yaml"),
            ["LauncherSettings.yaml"] = Path.Combine("Preferences", "LauncherSettings.yaml"),
            ["EditorBootstrap.log"] = Path.Combine("Logs", "EditorBootstrap.log"),
            ["LauncherBuild.log"] = Path.Combine("Logs", "LauncherBuild.log")
        };

        foreach (var source in Directory.EnumerateFiles(legacyRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(legacyRoot, source);
            var destinationRelative = knownFiles.GetValueOrDefault(relative,
                Path.Combine("MigratedLegacy", relative));
            try
            {
                MovePreservingExisting(source, Path.Combine(destinationRoot, destinationRelative));
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
        var preferencesDirectory = Path.Combine(root, "Preferences");
        foreach (var fileName in new[] { "Preferences.yaml", "EditorPrefs.yaml", "LauncherSettings.yaml" })
        {
            var source = Path.Combine(root, fileName);
            if (!File.Exists(source)) continue;
            try
            {
                MovePreservingExisting(source, Path.Combine(preferencesDirectory, fileName));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A running editor can briefly own the file. Retry on the next launch.
            }
        }
    }

    private static void MovePreservingExisting(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            var backupDirectory = Path.Combine(Path.GetDirectoryName(destination)!, "MigratedLegacy");
            Directory.CreateDirectory(backupDirectory);
            destination = Path.Combine(backupDirectory,
                $"{Path.GetFileNameWithoutExtension(destination)}-{DateTime.UtcNow:yyyyMMddHHmmssfff}" +
                Path.GetExtension(destination));
        }

        File.Copy(source, destination, overwrite: false);
        File.Delete(source);
    }

    private static void DeleteEmptyDirectories(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
}
