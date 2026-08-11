using BEngine.Serialization;
using BEngine.Serialization.Documents;

namespace BEngine.ProjectSystem;

public sealed record BPackageDefinition(string Path, PackageDefinitionDocument Document);

public static class PackageDefinitionLoader
{
    public static PackageDefinitionDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        return Normalize(YamlUtility.Load<PackageDefinitionDocument>(fullPath), fullPath);
    }

    public static PackageDefinitionDocument Normalize(
        PackageDefinitionDocument document,
        string sourceName = "package.yaml")
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.Format.Equals("BEngine.Package", StringComparison.Ordinal))
            throw Invalid(sourceName, $"unsupported format '{document.Format}'");
        if (document.Version is not (1 or 2))
            throw Invalid(sourceName, $"unsupported version {document.Version}");
        if (string.IsNullOrWhiteSpace(document.Id))
            throw Invalid(sourceName, "id is required");

        var runtime = document.Runtime;
        var editor = document.Editor;
        if (document.Version == 1)
        {
            if (string.IsNullOrWhiteSpace(document.Assembly))
                throw Invalid(sourceName, "v1 assembly is required");
            runtime = new PackageAssemblyDocument
            {
                Assembly = document.Assembly.Trim(),
                RootNamespace = string.IsNullOrWhiteSpace(document.Namespace)
                    ? document.Assembly.Trim()
                    : document.Namespace.Trim(),
                Dependencies = (document.Dependencies ?? [])
                    .Select(packageId => new PackageDependencyDocument
                    {
                        PackageId = packageId,
                        Target = "runtime"
                    })
                    .ToList()
            };
        }
        else if (document.Assembly is not null || document.Namespace is not null ||
                 document.Dependencies is not null)
        {
            throw Invalid(sourceName, "v2 cannot mix legacy assembly, namespace or dependencies fields");
        }

        if (runtime is null && editor is null)
            throw Invalid(sourceName, "at least one runtime or editor assembly is required");

        runtime = NormalizeAssembly(runtime, "runtime", sourceName, allowEditorDependency: false);
        editor = NormalizeAssembly(editor, "editor", sourceName, allowEditorDependency: true);

        return new PackageDefinitionDocument
        {
            Format = "BEngine.Package",
            Version = 2,
            Id = document.Id.Trim(),
            PackageVersion = string.IsNullOrWhiteSpace(document.PackageVersion)
                ? "1.0.0"
                : document.PackageVersion.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(document.DisplayName)
                ? document.Id.Trim()
                : document.DisplayName.Trim(),
            Description = document.Description?.Trim() ?? string.Empty,
            EnabledByDefault = document.EnabledByDefault,
            Required = document.Required,
            Runtime = runtime,
            Editor = editor
        };
    }

    private static PackageAssemblyDocument? NormalizeAssembly(
        PackageAssemblyDocument? assembly,
        string kind,
        string sourceName,
        bool allowEditorDependency)
    {
        if (assembly is null) return null;
        if (string.IsNullOrWhiteSpace(assembly.Assembly))
            throw Invalid(sourceName, $"{kind}.assembly is required");
        if (string.IsNullOrWhiteSpace(assembly.RootNamespace))
            throw Invalid(sourceName, $"{kind}.rootNamespace is required");

        var dependencies = new List<PackageDependencyDocument>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in assembly.Dependencies ?? [])
        {
            if (string.IsNullOrWhiteSpace(dependency.PackageId))
                throw Invalid(sourceName, $"{kind} dependency packageId is required");
            var target = dependency.Target?.Trim().ToLowerInvariant();
            if (target is not ("runtime" or "editor"))
                throw Invalid(sourceName,
                    $"{kind} dependency '{dependency.PackageId}' has invalid target '{dependency.Target}'");
            if (!allowEditorDependency && target == "editor")
                throw Invalid(sourceName,
                    $"runtime assembly cannot depend on editor assembly '{dependency.PackageId}'");

            var packageId = dependency.PackageId.Trim();
            if (!seen.Add($"{packageId}\0{target}"))
                throw Invalid(sourceName, $"duplicate {kind} dependency '{packageId}' ({target})");
            dependencies.Add(new PackageDependencyDocument
            {
                PackageId = packageId,
                Target = target,
                Optional = dependency.Optional
            });
        }

        return new PackageAssemblyDocument
        {
            Assembly = assembly.Assembly.Trim(),
            RootNamespace = assembly.RootNamespace.Trim(),
            Dependencies = dependencies
        };
    }

    private static InvalidDataException Invalid(string sourceName, string message) =>
        new($"Invalid package definition '{sourceName}': {message}.");
}

public sealed class BPackageCatalog
{
    private readonly Dictionary<string, BPackageDefinition> _definitions =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<BPackageDefinition> packages => _definitions.Values
        .OrderBy(package => package.Document.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public BPackageCatalog(IEnumerable<string> definitionPaths)
    {
        ArgumentNullException.ThrowIfNull(definitionPaths);
        foreach (var path in definitionPaths.Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var definition = PackageDefinitionLoader.Load(path);
            if (!_definitions.TryAdd(definition.Id, new BPackageDefinition(path, definition)))
                throw new InvalidDataException($"Duplicate package id '{definition.Id}' in '{path}'.");
        }
        ValidateGraph();
    }

    public static BPackageCatalog Discover(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var paths = Directory.EnumerateFiles(workspace.PackagesPath, "package.yaml",
            SearchOption.AllDirectories).ToList();

        var installedPackagesPath = Path.Combine(AppContext.BaseDirectory, "Packages");
        var installedDefinitions = Directory.Exists(installedPackagesPath)
            ? Directory.EnumerateFiles(installedPackagesPath, "package.yaml", SearchOption.AllDirectories).ToArray()
            : [];
        if (installedDefinitions.Length > 0)
        {
            paths.AddRange(installedDefinitions);
        }
        else
        {
            var repositoryRoot = FindRepositoryRoot();
            if (repositoryRoot is not null)
            {
                paths.AddRange(Directory.EnumerateDirectories(Path.Combine(repositoryRoot, "src"))
                    .Select(directory => Path.Combine(directory, "package.yaml"))
                    .Where(File.Exists));
            }
        }

        return new BPackageCatalog(paths);
    }

    public bool TryGet(string packageId, out BPackageDefinition definition) =>
        _definitions.TryGetValue(packageId, out definition!);

    public BPackageDefinition GetRequired(string packageId) => TryGet(packageId, out var definition)
        ? definition
        : throw new KeyNotFoundException($"Package definition '{packageId}' was not found.");

    public IReadOnlyList<string> GetDependencyClosure(string packageId)
    {
        var result = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { packageId };
        AddDependencies(packageId, visited, result);
        return result;
    }

    public IReadOnlyList<string> GetDependentClosure(string packageId)
    {
        var result = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { packageId };
        AddDependents(packageId, visited, result);
        return result;
    }

    private void AddDependencies(string packageId, ISet<string> visited, ICollection<string> result)
    {
        if (!TryGet(packageId, out var definition)) return;
        foreach (var dependency in DependenciesOf(definition.Document))
        {
            if (!visited.Add(dependency)) continue;
            AddDependencies(dependency, visited, result);
            result.Add(dependency);
        }
    }

    private void AddDependents(string packageId, ISet<string> visited, ICollection<string> result)
    {
        foreach (var dependent in _definitions.Values
                     .Where(candidate => DependenciesOf(candidate.Document).Contains(
                         packageId, StringComparer.OrdinalIgnoreCase))
                     .OrderBy(candidate => candidate.Document.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (!visited.Add(dependent.Document.Id)) continue;
            AddDependents(dependent.Document.Id, visited, result);
            result.Add(dependent.Document.Id);
        }
    }

    private void ValidateGraph()
    {
        var assemblyKinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in _definitions.Values)
        {
            AddAssemblyKind(package, "runtime", package.Document.Runtime, assemblyKinds);
            AddAssemblyKind(package, "editor", package.Document.Editor, assemblyKinds);
            ValidateDependencyTargets(package);
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in _definitions.Values)
        {
            VisitAssembly(package.Document.Id, "runtime", package.Document.Runtime, visited, active);
            VisitAssembly(package.Document.Id, "editor", package.Document.Editor, visited, active);
        }
    }

    private static void AddAssemblyKind(
        BPackageDefinition package,
        string kind,
        PackageAssemblyDocument? assembly,
        IDictionary<string, string> kinds)
    {
        if (assembly is null) return;
        if (kinds.TryGetValue(assembly.Assembly, out var declaredKind) &&
            !declaredKind.Equals(kind, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Assembly '{assembly.Assembly}' cannot contain both runtime and editor package code " +
                $"(latest: '{package.Document.Id}' {kind}).");
        kinds[assembly.Assembly] = kind;
    }

    private void ValidateDependencyTargets(BPackageDefinition package)
    {
        foreach (var dependency in EnumerateDependencies(package.Document))
        {
            if (!TryGet(dependency.PackageId, out var target))
                throw new InvalidDataException($"Package '{package.Document.Id}' depends on missing package " +
                                               $"'{dependency.PackageId}'.");
            var targetAssembly = dependency.Target == "runtime"
                ? target.Document.Runtime
                : target.Document.Editor;
            if (targetAssembly is null)
                throw new InvalidDataException($"Package '{package.Document.Id}' depends on missing " +
                                               $"{dependency.Target} assembly of '{dependency.PackageId}'.");
        }
    }

    private void VisitAssembly(
        string packageId,
        string target,
        PackageAssemblyDocument? assembly,
        ISet<string> visited,
        ISet<string> active)
    {
        if (assembly is null) return;
        var key = $"{packageId}\0{target}";
        if (visited.Contains(key)) return;
        if (!active.Add(key))
            throw new InvalidDataException($"Package assembly dependency cycle detected at '{packageId}' ({target}).");

        foreach (var dependency in assembly.Dependencies)
        {
            var targetPackage = GetRequired(dependency.PackageId).Document;
            VisitAssembly(targetPackage.Id, dependency.Target,
                dependency.Target == "runtime" ? targetPackage.Runtime : targetPackage.Editor,
                visited, active);
        }
        active.Remove(key);
        visited.Add(key);
    }

    private static IEnumerable<PackageDependencyDocument> EnumerateDependencies(
        PackageDefinitionDocument package) =>
        (package.Runtime?.Dependencies ?? []).Concat(package.Editor?.Dependencies ?? []);

    private static IEnumerable<string> DependenciesOf(PackageDefinitionDocument package) =>
        EnumerateDependencies(package)
            .Where(dependency => !dependency.Optional)
            .Select(dependency => dependency.PackageId)
            .Where(packageId => !packageId.Equals(package.Id, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(packageId => packageId, StringComparer.OrdinalIgnoreCase);

    private static string? FindRepositoryRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var directory = new DirectoryInfo(Path.GetFullPath(start)); directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "BEngine.sln")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "src")))
                    return directory.FullName;
            }
        }
        return null;
    }
}
