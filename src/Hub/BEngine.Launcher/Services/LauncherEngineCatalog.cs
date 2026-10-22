namespace BEngine.Launcher;

internal sealed class LauncherEngineCatalog(string? baseDirectory = null)
{
    internal string ResolveEnginesRoot() => Path.Combine(LauncherInstallationPaths.ResolveRoot(baseDirectory), "BEgine");

    internal IReadOnlyList<LauncherEngineInfo> Discover(string? enginesRoot = null)
    {
        var root = Path.GetFullPath(enginesRoot ?? ResolveEnginesRoot());
        if (!Directory.Exists(root)) return [];

        return Directory.EnumerateDirectories(root, "BEgine-*", SearchOption.TopDirectoryOnly)
            .Where(directory => File.Exists(Path.Combine(directory, "BEngine.Editor.exe")))
            .Select(directory => new LauncherEngineInfo
            {
                Version = Path.GetFileName(directory)["BEgine-".Length..],
                DirectoryPath = directory,
                Description = ReadIntroduction(directory)
            })
            .OrderByDescending(engine => Version.TryParse(engine.Version.Split('-')[0], out var version) ? version : new Version())
            .ThenBy(engine => engine.Version.Contains('-'))
            .ThenByDescending(engine => engine.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ReadIntroduction(string directory)
    {
        var readme = Path.Combine(directory, "Editor", "Readme.md");
        if (!File.Exists(readme)) return "No introduction is included with this engine version.";
        try
        {
            var lines = new List<string>();
            foreach (var line in File.ReadLines(readme))
            {
                var text = line.Trim();
                if (text.Length == 0 || text.StartsWith('#') || text.StartsWith("![", StringComparison.Ordinal))
                {
                    if (lines.Count > 0) break;
                    continue;
                }
                lines.Add(text.Replace("`", string.Empty));
            }
            return lines.Count > 0 ? string.Join(" ", lines) : "No introduction is included with this engine version.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "The introduction could not be read.";
        }
    }
}
