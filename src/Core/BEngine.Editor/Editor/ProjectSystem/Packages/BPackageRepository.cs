namespace BEngine.ProjectSystem;

public static class BPackageRepository
{
    public static string rootPath => ResolveRootPath();

    public static IReadOnlyList<string> DiscoverDefinitionPaths()
    {
        var root = rootPath;
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "package.yaml", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
    }

    private static string ResolveRootPath()
    {
        var configured = Environment.GetEnvironmentVariable("BENGINE_PACKAGES_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);

        var candidates = new List<string>();
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var directory = new DirectoryInfo(Path.GetFullPath(start)); directory is not null;
                 directory = directory.Parent)
            {
                if (directory.Name.Equals("Output", StringComparison.OrdinalIgnoreCase))
                    candidates.Add(Path.Combine(directory.FullName, "Packages"));
                candidates.Add(Path.Combine(directory.FullName, "Output", "Packages"));
            }
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(Directory.Exists) ??
               Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Packages"));
    }
}
