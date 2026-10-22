using BEngine.ProjectSystem;

namespace BEngine.Launcher;

internal sealed class LauncherPackageCatalog(string? baseDirectory = null)
{
    internal static string? FindDocumentation(string packageRoot)
    {
        var path = Path.Combine(packageRoot, "Editor", "Doc", "index.html");
        return File.Exists(path) ? Path.GetFullPath(path) : null;
    }

    private static string[] ReadExampleNames(string definitionPath)
    {
        var directory = Path.GetDirectoryName(definitionPath)!;
        var examples = Path.Combine(directory, "Editor", "Examples");
        return Directory.Exists(examples) ? Directory.GetFiles(examples, "*.bpackage")
            .Select(path => Path.GetFileNameWithoutExtension(path)).Order().ToArray() : [];
    }

    internal string ResolvePackagesRoot() => Path.Combine(LauncherInstallationPaths.ResolveRoot(baseDirectory), "Packages");

    internal IReadOnlyList<LauncherPackageInfo> Discover(string? packagesRoot = null, string? engineVersion = null)
    {
        engineVersion ??= EngineVersion.Current;
        BPackageVersion.Validate(engineVersion);
        var root = Path.GetFullPath(packagesRoot ?? ResolvePackagesRoot());
        if (!Directory.Exists(root)) return [];

        var definitions = new List<BPackageDefinition>();
        foreach (var definitionPath in Directory.EnumerateFiles(root, "package.yaml", SearchOption.AllDirectories)
                     .Where(path => !Path.GetRelativePath(root, path).Split('/', '\\').Any(segment => segment.StartsWith('.')))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var definition = PackageDefinitionLoader.Load(definitionPath);
            if (!BPackageCompatibility.SupportsEngine(definition, engineVersion)) continue;
            definitions.Add(new BPackageDefinition(definitionPath, definition));
        }

        return BPackageReleaseResolver.Resolve(definitions).Select(release =>
        {
            var definitionPath = release.Path;
            var definition = release.Document;
            return new LauncherPackageInfo
            {
                Id = definition.Id,
                Type = definition.Type,
                DisplayName = definition.DisplayName,
                Version = definition.PackageVersion,
                MinimumEngineVersion = definition.MinimumEngineVersion,
                Description = definition.Description,
                DirectoryPath = Path.GetDirectoryName(definitionPath)!,
                RuntimeAssembly = definition.Runtime?.Assembly,
                EditorAssembly = definition.Editor?.Assembly,
                ProjectDirectory = definition.ProjectDirectory,
                ExampleNames = ReadExampleNames(definitionPath),
                HasDocumentation = FindDocumentation(Path.GetDirectoryName(definitionPath)!) is not null,
                HasRuntime = definition.Runtime is not null,
                HasEditor = definition.Editor is not null,
                Dependencies = (definition.Runtime?.Dependencies ?? [])
                    .Select(dependency => new LauncherPackageDependency(dependency.PackageId, "runtime", dependency.Target, dependency.Optional))
                    .Concat((definition.Editor?.Dependencies ?? []).Select(dependency =>
                        new LauncherPackageDependency(dependency.PackageId, "editor", dependency.Target, dependency.Optional)))
                    .ToArray()
            };
        })
            .OrderBy(package => package.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
