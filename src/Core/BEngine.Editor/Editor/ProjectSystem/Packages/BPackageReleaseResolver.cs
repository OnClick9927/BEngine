using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem;

/// <summary>Selects one consistent set of package releases for an engine's compatible catalog.</summary>
public static class BPackageReleaseResolver
{
    public static IReadOnlyList<BPackageDefinition> Resolve(IEnumerable<BPackageDefinition> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var versions = packages.GroupBy(package => package.Document.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group
                .OrderByDescending(package => package.Document.PackageVersion, BPackageVersion.Comparer).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selected = new Dictionary<string, BPackageDefinition>(StringComparer.OrdinalIgnoreCase);
        var branches = 0;
        foreach (var id in versions.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            roots.Add(id);
            if (Search(new Dictionary<string, BPackageDefinition>(StringComparer.OrdinalIgnoreCase), roots,
                    out var resolved)) selected = resolved;
            else roots.Remove(id);
        }
        return selected.Values.OrderBy(package => package.Document.Id, StringComparer.OrdinalIgnoreCase).ToArray();

        bool Search(Dictionary<string, BPackageDefinition> assignments, HashSet<string> required,
            out Dictionary<string, BPackageDefinition> resolved)
        {
            if (++branches > 20000)
                throw new InvalidDataException("Package version resolution exceeded its search limit. Narrow conflicting package releases or dependency declarations.");
            resolved = assignments;
            var constraints = assignments.Values.SelectMany(package => Dependencies(package.Document))
                .Where(dependency => !dependency.Optional).ToLookup(dependency => dependency.PackageId,
                    StringComparer.OrdinalIgnoreCase);
            if (assignments.Values.Any(package => constraints[package.Document.Id]
                    .Any(dependency => !HasAssembly(package, dependency.Target)))) return false;
            var pending = required.Where(id => !assignments.ContainsKey(id))
                .Select(id => (Id: id, Candidates: versions.TryGetValue(id, out var releases)
                    ? releases.Where(package => constraints[id].All(dependency => HasAssembly(package, dependency.Target))).ToArray() : []))
                .OrderBy(item => item.Candidates.Length).ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (pending.Id is null) return !HasAssemblyCycle(assignments);
            foreach (var candidate in pending.Candidates)
            {
                var next = new Dictionary<string, BPackageDefinition>(assignments, StringComparer.OrdinalIgnoreCase)
                    { [candidate.Document.Id] = candidate };
                var dependencies = new HashSet<string>(required, StringComparer.OrdinalIgnoreCase);
                foreach (var dependency in Dependencies(candidate.Document).Where(dependency => !dependency.Optional))
                    dependencies.Add(dependency.PackageId);
                if (Search(next, dependencies, out resolved)) return true;
            }
            return false;
        }
    }

    private static bool HasAssembly(BPackageDefinition package, string target) =>
        (target == "runtime" ? package.Document.Runtime : package.Document.Editor) is not null;

    private static IEnumerable<PackageDependencyDocument> Dependencies(PackageDefinitionDocument package) =>
        (package.Runtime?.Dependencies ?? []).Concat(package.Editor?.Dependencies ?? []);

    private static bool HasAssemblyCycle(IReadOnlyDictionary<string, BPackageDefinition> packages)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Visit(BPackageDefinition package, string targetKind, PackageAssemblyDocument? assembly)
        {
            if (assembly is null) return false;
            var key = package.Document.Id + "\0" + targetKind;
            if (visited.Contains(key)) return false;
            if (!active.Add(key)) return true;
            foreach (var dependency in assembly.Dependencies)
                if (packages.TryGetValue(dependency.PackageId, out var target) &&
                    Visit(target, dependency.Target, dependency.Target == "runtime" ? target.Document.Runtime : target.Document.Editor))
                    return true;
            active.Remove(key);
            visited.Add(key);
            return false;
        }
        return packages.Values.Any(package => Visit(package, "runtime", package.Document.Runtime) ||
            Visit(package, "editor", package.Document.Editor));
    }
}
