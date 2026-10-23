namespace BEngine.Launcher;

internal static class LauncherDataPaths
{
    private static readonly Lazy<string> Root = new(Initialize);
    internal static string LauncherSettingsPath => Path.Combine(Root.Value, "Preferences", "Hub", "LauncherSettings.yaml");
    internal static string LogsPath => EnsureDirectory(Path.Combine(LauncherInstallationPaths.ResolveTemporaryDirectory(Root.Value), "Logs"));

    private static string Initialize()
    {
        var configured = Environment.GetEnvironmentVariable("BENGINE_HUB_DIRECTORY") is { Length: > 0 } hubDirectory
            ? hubDirectory
            : Environment.GetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH");
        var root = string.IsNullOrWhiteSpace(configured) ? LauncherInstallationPaths.ResolveRoot() : Path.GetFullPath(configured);
        var preferences = EnsureDirectory(Path.Combine(root, "Preferences", "Hub"));
        var destination = Path.Combine(preferences, "LauncherSettings.yaml");
        // Preserve the Editor's existing Hub preference migration order. Other
        // engine preferences remain owned and migrated by their engine version.
        var sources = new List<string> { Path.Combine(root, "Preferences", "LauncherSettings.yaml") };
        var legacy = Path.Combine(root, "EditorData");
        if (Directory.Exists(legacy))
            sources.AddRange(Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories).Where(path =>
            {
                var relative = Path.GetRelativePath(legacy, path).Replace('\\', '/');
                return relative is "LauncherSettings.yaml" or "Preferences/LauncherSettings.yaml";
            }));
        sources.Add(Path.Combine(root, "LauncherSettings.yaml"));
        sources.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BEngine", "LauncherSettings.yaml"));
        foreach (var source in sources)
        {
            try
            {
                if (!File.Exists(source)) continue;
                var target = destination;
                if (File.Exists(target))
                    target = Path.Combine(EnsureDirectory(Path.Combine(preferences, "MigratedLegacy")),
                        $"LauncherSettings-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.yaml");
                File.Copy(source, target, false);
                File.Delete(source);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Trace.WriteLine(exception); }
        }
        return root;
    }

    internal static string GetStartupStatusPath(string projectRoot, string token)
    {
        if (!Guid.TryParseExact(token, "N", out var id)) throw new ArgumentException("The editor startup token must be a 32-character GUID.", nameof(token));
        return Path.Combine(EnsureDirectory(Path.Combine(NormalizeProjectRoot(projectRoot), "Library", "Editor", "Startup")), $"{id:N}.yaml");
    }

    internal static string GetProjectLogPath(string projectRoot, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.IndexOfAny(['/', '\\']) >= 0 || !fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The project log file name must be a simple .log file name.", nameof(fileName));
        return Path.Combine(EnsureDirectory(Path.Combine(NormalizeProjectRoot(projectRoot), "Library", "Editor", "Logs")), fileName);
    }

    private static string NormalizeProjectRoot(string projectRoot)
    {
        var path = Path.GetFullPath(projectRoot);
        return File.Exists(path) || Path.GetFileName(path).Equals(LauncherProjectWorkspace.ProjectFileName, StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(path)! : path;
    }

    private static string EnsureDirectory(string directory) { Directory.CreateDirectory(directory); return directory; }
}
