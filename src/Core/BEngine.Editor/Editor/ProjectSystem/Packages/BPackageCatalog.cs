using BEngine.Serialization;
using BEngine.Documents;
using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem;

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
        return DiscoverAvailable();
    }

    public static BPackageCatalog DiscoverAvailable() =>
        new(BPackageRepository.DiscoverDefinitionPaths());

    internal static BPackageCatalog DiscoverCached(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var cacheRoot = ProjectPackageCache.GetCacheRoot(workspace);
        var paths = Directory.Exists(cacheRoot)
            ? Directory.EnumerateFiles(cacheRoot, "package.yaml", SearchOption.AllDirectories)
            : [];
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
        var assemblyKinds = new Dictionary<string, (string PackageId, string Kind)>(StringComparer.OrdinalIgnoreCase);
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
        IDictionary<string, (string PackageId, string Kind)> kinds)
    {
        if (assembly is null) return;
        if (kinds.TryGetValue(assembly.Assembly, out var declared))
            throw new InvalidDataException(
                $"Assembly '{assembly.Assembly}' is already owned by package '{declared.PackageId}' " +
                $"({declared.Kind}) and cannot also be owned by '{package.Document.Id}' ({kind}).");
        kinds[assembly.Assembly] = (package.Document.Id, kind);
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

}
