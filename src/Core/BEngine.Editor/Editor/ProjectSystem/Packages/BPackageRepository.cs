namespace BEngine.ProjectSystem;

public static class BPackageRepository
{
    public static string rootPath => ResolveRootPath();

    public static IReadOnlyList<string> DiscoverDefinitionPaths() => DiscoverDefinitionPaths(rootPath);

    public static IReadOnlyList<string> DiscoverDefinitionPaths(string directory)
    {
        var root = EditorHostServices.Paths.GetFullPath(directory);
        return EditorHostServices.FileSystem.DirectoryExists(root)
            ? EditorHostServices.FileSystem.EnumerateFiles(root, "package.yaml", SearchOption.AllDirectories)
                .Where(path => !EditorHostServices.Paths.GetRelativePath(root, path).Split('/', '\\')
                    .Any(segment => segment.StartsWith('.') || segment is "bin" or "obj"))
                .Select(EditorHostServices.Paths.GetFullPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
    }

    private static string ResolveRootPath()
    {
        var configured = EditorHostServices.Platform.GetEnvironmentVariable("BENGINE_PACKAGES_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return EditorHostServices.Paths.GetFullPath(configured);

        var candidates = new List<string>();
        foreach (var start in new[] { EditorHostServices.Platform.BaseDirectory, EditorHostServices.Platform.CurrentDirectory })
        {
            for (var directory = EditorHostServices.Paths.GetFullPath(start);
                 directory is not null;
                 directory = EditorHostServices.Paths.GetDirectoryName(directory))
            {
                var name = EditorHostServices.Paths.GetFileName(
                    EditorHostServices.Paths.TrimEndingDirectorySeparator(directory));
                if (name.Equals("Engine", StringComparison.OrdinalIgnoreCase))
                    candidates.Add(EditorHostServices.Paths.Combine(directory, "Packages"));
                candidates.Add(EditorHostServices.Paths.Combine(directory, "Engine", "Engine", "Packages"));
                candidates.Add(EditorHostServices.Paths.Combine(directory, "Engine", "Packages"));
            }
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(EditorHostServices.FileSystem.DirectoryExists) ??
               EditorHostServices.Paths.GetFullPath(EditorHostServices.Paths.Combine(EditorHostServices.Platform.BaseDirectory, "..", "Packages"));
    }
}
