using BEngine.ProjectSystem;

namespace BEngine.Launcher;

internal sealed class LauncherPackageCatalog
{
    internal string ResolvePackagesRoot()
    {
        var configured = Environment.GetEnvironmentVariable("BENGINE_PACKAGES_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
        {
            if (directory.Name.Equals("Output", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(directory.FullName, "Packages");
            if (Directory.Exists(Path.Combine(directory.FullName, "src")) &&
                Directory.Exists(Path.Combine(directory.FullName, "Output")))
                return Path.Combine(directory.FullName, "Output", "Packages");
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Packages"));
    }

    internal IReadOnlyList<LauncherPackageInfo> Discover(string? packagesRoot = null)
    {
        var root = Path.GetFullPath(packagesRoot ?? ResolvePackagesRoot());
        if (!Directory.Exists(root)) return [];

        var packages = new List<LauncherPackageInfo>();
        foreach (var definitionPath in Directory.EnumerateFiles(root, "package.yaml", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var definition = PackageDefinitionLoader.Load(definitionPath);
            packages.Add(new LauncherPackageInfo
            {
                Id = definition.Id,
                DisplayName = definition.DisplayName,
                Version = definition.PackageVersion,
                Description = definition.Description,
                DirectoryPath = Path.GetDirectoryName(definitionPath)!,
                HasRuntime = definition.Runtime is not null,
                HasEditor = definition.Editor is not null
            });
        }

        return packages
            .GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(package => package.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
