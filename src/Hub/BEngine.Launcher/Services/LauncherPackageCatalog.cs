using BEngine.ProjectSystem;

namespace BEngine.Launcher;

internal sealed class LauncherPackageCatalog(string? baseDirectory = null, string? packagesRoot = null) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LauncherPackageIndex> _indexes = new(StringComparer.OrdinalIgnoreCase);
    internal event Action? Changed;

    internal void Start() => _ = GetIndex(ResolvePackagesRoot());
    internal void Refresh(string root) => GetIndex(root).Refresh();
    internal IReadOnlyList<BPackageDefinition> GetDefinitions(string root) => GetIndex(root).GetDefinitions();

    private LauncherPackageIndex GetIndex(string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        lock (_gate)
        {
            if (_indexes.TryGetValue(root, out var index)) return index;
            index = new LauncherPackageIndex(root);
            index.Changed += OnChanged;
            _indexes.Add(root, index);
            return index;
        }
    }

    private void OnChanged() => Changed?.Invoke();

    internal PackageCatalogSnapshot GetSnapshot(string root, string engineVersion) =>
        GetIndex(root).GetSnapshot(engineVersion);

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var index in _indexes.Values) index.Dispose();
            _indexes.Clear();
        }
    }
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

    internal string ResolvePackagesRoot() => Path.GetFullPath(packagesRoot ??
        Path.Combine(LauncherInstallationPaths.ResolveRoot(baseDirectory), "Packages"));

    internal IReadOnlyList<LauncherPackageInfo> Discover(string? packagesRoot = null, string? engineVersion = null)
        => GetIndex(packagesRoot ?? ResolvePackagesRoot()).GetPackages(engineVersion ?? EngineVersion.Current);

    internal static IReadOnlyList<LauncherPackageInfo> BuildPackageInfos(IEnumerable<BPackageDefinition> definitions,
        IReadOnlyDictionary<string, PackageDistributionKind>? distribution = null)
    {
        var releases = BPackageReleaseResolver.Resolve(definitions);
        // Required is a policy for the package identity, including when release
        // resolution chooses an older compatible version without the flag.
        var roots = definitions.Where(release => release.Document.Required)
            .Select(release => release.Document.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredBy = ResolveRequirements(releases, roots);
        LauncherPackageValidation.ValidateRequiredMetadata(releases, requiredBy.Keys);
        return releases.Select(release =>
        {
            var definitionPath = release.Path;
            var definition = release.Document;
            return new LauncherPackageInfo
            {
                Id = definition.Id,
                Distribution = distribution?.GetValueOrDefault(definitionPath) ?? PackageDistributionKind.Unknown,
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
                Required = roots.Contains(definition.Id),
                RequiredBy = requiredBy.GetValueOrDefault(definition.Id, []),
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

    private static Dictionary<string, string[]> ResolveRequirements(
        IReadOnlyList<BPackageDefinition> releases, IReadOnlySet<string> roots)
    {
        var packages = BuildIndex4896(releases, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition> BuildIndex4896(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.BPackageDefinition> indexSource4896, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer4896)
        {
            ArgumentNullException.ThrowIfNull(indexSource4896, "source");
            var knownCount = indexSource4896.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(indexComparer4896);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.BPackageDefinition> values;
            if (knownCount && indexSource4896 is global::BEngine.ProjectSystem.BPackageDefinition[] array)
                values = array;
            else if (knownCount && indexSource4896 is global::System.Collections.Generic.List<global::BEngine.ProjectSystem.BPackageDefinition> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(capacity, indexComparer4896);
                foreach (var item in indexSource4896)
                    result.Add(IndexKey4896(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(values.Length, indexComparer4896);
                foreach (var item in values)
                    result.Add(IndexKey4896(item), item);
                return result;
            }
        
            string IndexKey4896(global::BEngine.ProjectSystem.BPackageDefinition release) => release.Document.Id;
        }
        var requiredBy = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Order(StringComparer.OrdinalIgnoreCase))
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Visit(root);
            void Visit(string id)
            {
                if (!visited.Add(id)) return;
                if (!packages.TryGetValue(id, out var package))
                    throw new InvalidDataException($"Required package '{root}' cannot be installed: package '{id}' has no compatible, resolvable release.");
                if (!requiredBy.TryGetValue(id, out var owners)) requiredBy.Add(id, owners = []);
                owners.Add(root);
                {
                    var __filteredSource5737 = (package.Document.Runtime?.Dependencies ?? []).Concat(package.Document.Editor?.Dependencies ?? []);
                    ArgumentNullException.ThrowIfNull(__filteredSource5737, "source");
                    foreach (var dependency in __filteredSource5737)
                    {
                        if (!(!dependency.Optional))
                            continue;
                        if (!packages.TryGetValue(dependency.PackageId, out var target) || (dependency.Target == "runtime" ? target.Document.Runtime : target.Document.Editor)is null)
                            throw new InvalidDataException($"Required package '{root}' cannot be installed: '{id}' needs '{dependency.PackageId}' ({dependency.Target}).");
                        Visit(dependency.PackageId);
                    }
                }
            }
        }
        return BuildIndex6642(requiredBy, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, string[]> BuildIndex6642(global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, global::System.Collections.Generic.List<string>>> indexSource6642, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer6642)
        {
            ArgumentNullException.ThrowIfNull(indexSource6642, "source");
            var knownCount = indexSource6642.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, string[]>(indexComparer6642);
            global::System.ReadOnlySpan<global::System.Collections.Generic.KeyValuePair<string, global::System.Collections.Generic.List<string>>> values;
            if (knownCount && indexSource6642 is global::System.Collections.Generic.KeyValuePair<string, global::System.Collections.Generic.List<string>>[] array)
                values = array;
            else if (knownCount && indexSource6642 is global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<string, global::System.Collections.Generic.List<string>>> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string[]>(capacity, indexComparer6642);
                foreach (var item in indexSource6642)
                    result.Add(IndexKey6642(item), IndexValue6642(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string[]>(values.Length, indexComparer6642);
                foreach (var item in values)
                    result.Add(IndexKey6642(item), IndexValue6642(item));
                return result;
            }
        
            string IndexKey6642(global::System.Collections.Generic.KeyValuePair<string, global::System.Collections.Generic.List<string>> item) => item.Key;
            string[] IndexValue6642(global::System.Collections.Generic.KeyValuePair<string, global::System.Collections.Generic.List<string>> item) => item.Value.ToArray();
        }
    }
}
