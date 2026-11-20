using BEngine.Serialization;
using BEngine.Editor.Documents;
using BEngine.Editor;

namespace BEngine.ProjectSystem;

public sealed class BPackageCatalog
{
    private readonly Dictionary<string, BPackageDefinition> _definitions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<BPackageDefinition>> _versions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<BPackageDefinition>> _versionViews = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<BPackageDefinition> _orderedPackages;
    private readonly Lock _packageViewGate = new();

    public IReadOnlyList<BPackageDefinition> GetVersions(string packageId) =>
        _versionViews.TryGetValue(packageId, out var versions) ? versions : [];

    internal IEnumerable<BPackageDefinition> EnumerateReleases() => _versions.Values.SelectMany(static versions => versions);

    public bool TryGet(string packageId, string version, out BPackageDefinition definition)
    {
        if (_versions.TryGetValue(packageId, out var versions))
            foreach (var candidate in versions)
                if (candidate.Document.PackageVersion == version)
                {
                    definition = candidate;
                    return true;
                }
        definition = null!;
        return false;
    }

    public IReadOnlyList<BPackageDefinition> packages
    {
        get
        {
            lock (_packageViewGate)
            {
                // Documents are publicly mutable; retain live ID ordering without
                // sorting and allocating a new array on every ordinary query.
                for (var index = 1; index < _orderedPackages.Count; index++)
                    if (StringComparer.OrdinalIgnoreCase.Compare(_orderedPackages[index - 1].Document.Id,
                            _orderedPackages[index].Document.Id) > 0)
                    {
                        _orderedPackages = Array.AsReadOnly(_definitions.Values
                            .OrderBy(package => package.Document.Id, StringComparer.OrdinalIgnoreCase).ToArray());
                        break;
                    }
                return _orderedPackages;
            }
        }
    }

    public BPackageCatalog(IEnumerable<string> definitionPaths, string? engineVersion = null, bool validateGraph = true)
        : this(LoadDefinitions(definitionPaths), engineVersion ?? EngineVersion.Current, validateGraph)
    {
    }

    private static IEnumerable<BPackageDefinition> LoadDefinitions(IEnumerable<string> definitionPaths)
    {
        ArgumentNullException.ThrowIfNull(definitionPaths);
        foreach (var path in definitionPaths.Select(EditorHostServices.Paths.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            yield return new BPackageDefinition(path, PackageDefinitionLoader.Load(path));
    }

    private BPackageCatalog(IEnumerable<BPackageDefinition> definitions, string engineVersion, bool validateGraph)
    {
        BPackageVersion.Validate(engineVersion);
        foreach (var release in definitions)
        {
            var path = release.Path;
            var definition = release.Document;
            if (!BPackageCompatibility.SupportsEngine(definition, engineVersion)) continue;
            if (!_versions.TryGetValue(definition.Id, out var versions))
                _versions.Add(definition.Id, versions = []);
            bool __queryResult3554;
            {
                var __querySource3554 = versions;
                ArgumentNullException.ThrowIfNull(__querySource3554, "source");
                __queryResult3554 = false;
                foreach (var __queryItem3554 in __querySource3554)
                {
                    if ((__queryItem3554.Document.PackageVersion == definition.PackageVersion))
                    {
                        __queryResult3554 = true;
                        break;
                    }
                }
            }
            
            if (__queryResult3554)
                throw new InvalidDataException($"Duplicate package '{definition.Id}' version '{definition.PackageVersion}' in '{path}'.");
            bool __queryResult3790;
            {
                var __querySource3790 = versions;
                ArgumentNullException.ThrowIfNull(__querySource3790, "source");
                __queryResult3790 = false;
                foreach (var __queryItem3790 in __querySource3790)
                {
                    if ((__queryItem3790.Document.Type != definition.Type))
                    {
                        __queryResult3790 = true;
                        break;
                    }
                }
            }
            
            if (__queryResult3790)
                throw new InvalidDataException($"Package '{definition.Id}' cannot change type between versions.");
            versions.Add(new BPackageDefinition(path, definition));
        }
        foreach (var (id, versions) in _versions)
        {
            versions.Sort((a, b) => BPackageVersion.Compare(b.Document.PackageVersion, a.Document.PackageVersion));
            _versionViews.Add(id, versions.AsReadOnly());
        }
        foreach (var package in BPackageReleaseResolver.Resolve(_versions.Values.SelectMany(versions => versions)))
            _definitions.Add(package.Document.Id, package);
        _orderedPackages = Array.AsReadOnly(_definitions.Values
            .OrderBy(package => package.Document.Id, StringComparer.OrdinalIgnoreCase).ToArray());
        if (validateGraph && _definitions.Count != _versions.Count)
            throw new InvalidDataException("The selected package releases have missing or conflicting mandatory dependencies, or an assembly dependency cycle.");
        if (validateGraph) ValidateGraph();
    }

    public static BPackageCatalog Discover(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return DiscoverAvailable();
    }

    public static BPackageCatalog DiscoverAvailable()
    {
        var snapshot = EditorServiceAccess.GetRequiredService<IPackageCatalogSource>()
            .GetSnapshot(BPackageRepository.rootPath, EngineVersion.Current);
        return new BPackageCatalog(snapshot.Packages.Select(item => new BPackageDefinition(item.Path, item.Document)),
            snapshot.EngineVersion, validateGraph: false);
    }

    public void ValidateEnabled(IEnumerable<string> ids)
    {
        var selected = ids.SelectMany(id => GetDependencyClosure(id).Append(id))
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(GetRequired).Select(item => item.Path);
        _ = new BPackageCatalog(selected);
    }

    internal static BPackageCatalog DiscoverCached(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var cacheRoot = ProjectPackageCache.GetCacheRoot(workspace);
        var paths = EditorHostServices.FileSystem.DirectoryExists(cacheRoot)
            ? EditorHostServices.FileSystem.EnumerateFiles(cacheRoot, "package.yaml", SearchOption.AllDirectories)
            : [];
        return new BPackageCatalog(paths);
    }

    public bool TryGet(string packageId, out BPackageDefinition definition) =>
        _definitions.TryGetValue(packageId, out definition!);

    public BPackageDefinition GetRequired(string packageId) => TryGet(packageId, out var definition)
        ? definition
        : throw new KeyNotFoundException($"Package definition '{packageId}' was not found.");

    /// <summary>Required is an identity policy across the compatible releases in this catalog.</summary>
    public IReadOnlyList<BPackageDefinition> GetRequiredPackageRoots() => _versions
        .Where(item => item.Value.Any(release => release.Document.Required))
        .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
        .Select(item => GetRequired(item.Key)).ToArray();

    /// <summary>Required roots and their nonoptional runtime/editor dependency closure.</summary>
    public IReadOnlyList<string> GetRequiredPackageIds() => GetRequiredPackageRoots()
        .SelectMany(root => GetDependencyClosure(root.Document.Id).Append(root.Document.Id))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
        .Select(id => GetRequired(id).Document.Id).ToArray();

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
        // Documents are mutable. Build a query-local reverse graph rather than
        // retaining stale edges, or scanning every definition at every depth.
        var dependents = new Dictionary<string, List<BPackageDefinition>>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in _definitions.Values)
        foreach (var dependency in DependenciesOf(definition.Document))
        {
            if (!dependents.TryGetValue(dependency, out var list)) dependents.Add(dependency, list = []);
            list.Add(definition);
        }
        AddDependents(packageId, visited, result, dependents);
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

    private static void AddDependents(string packageId, ISet<string> visited, ICollection<string> result,
        IReadOnlyDictionary<string, List<BPackageDefinition>> dependents)
    {
        if (!dependents.TryGetValue(packageId, out var entries)) return;
        foreach (var dependent in entries.OrderBy(candidate => candidate.Document.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (!visited.Add(dependent.Document.Id)) continue;
            AddDependents(dependent.Document.Id, visited, result, dependents);
            result.Add(dependent.Document.Id);
        }
    }

    private void ValidateGraph()
    {
        var assemblyKinds = new Dictionary<string, (string PackageId, string Kind)>(StringComparer.OrdinalIgnoreCase);
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in _definitions.Values)
        {
            AddAssemblyKind(package, "runtime", package.Document.Runtime, assemblyKinds);
            AddAssemblyKind(package, "editor", package.Document.Editor, assemblyKinds);
            ValidateDependencyTargets(package);
            foreach (var legacy in package.Document.Replaces)
            {
                if (_definitions.ContainsKey(legacy) || replacements.TryGetValue(legacy, out var owner) && owner != package.Document.Id)
                    throw new InvalidDataException($"Package replacement '{legacy}' has conflicting owners.");
                replacements[legacy] = package.Document.Id;
            }
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
        foreach (var name in assembly.Aliases.Append(assembly.Assembly))
        {
            if (kinds.TryGetValue(name, out var declared))
                throw new InvalidDataException(
                    $"Assembly name or alias '{name}' is already owned by package '{declared.PackageId}' " +
                    $"({declared.Kind}) and cannot also be owned by '{package.Document.Id}' ({kind}).");
            kinds[name] = (package.Document.Id, kind);
        }
    }

    private void ValidateDependencyTargets(BPackageDefinition package)
    {
        foreach (var dependency in EnumerateDependencies(package.Document))
        {
            if (!TryGet(dependency.PackageId, out var target))
            {
                if (dependency.Optional) continue;
                throw new InvalidDataException($"Package '{package.Document.Id}' depends on missing package " +
                                               $"'{dependency.PackageId}'.");
            }
            var targetAssembly = dependency.Target == "runtime"
                ? target.Document.Runtime
                : target.Document.Editor;
            if (targetAssembly is null)
            {
                if (dependency.Optional) continue;
                throw new InvalidDataException($"Package '{package.Document.Id}' depends on missing " +
                                               $"{dependency.Target} assembly of '{dependency.PackageId}'.");
            }
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
            if (dependency.Optional && !TryGet(dependency.PackageId, out _)) continue;
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
