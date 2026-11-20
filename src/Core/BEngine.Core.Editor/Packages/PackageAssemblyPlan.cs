using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem;

/// <summary>An assembly selected for activation. This data owns no code or assembly-load context.</summary>
public sealed record PackageAssemblyStep(BPackageDefinition Package, string Target, PackageAssemblyDocument Assembly);

/// <summary>Validates and orders a selected package set without file access, compilation or code loading.</summary>
public static class PackageAssemblyPlan
{
    public static IReadOnlyList<PackageAssemblyStep> Create(
        IEnumerable<BPackageDefinition> packages, bool includeEditor = true)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var definitions = new Dictionary<string, BPackageDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            ArgumentNullException.ThrowIfNull(package);
            if (!definitions.TryAdd(package.Document.Id, package))
                throw new InvalidDataException($"Multiple selected releases of package '{package.Document.Id}'.");
        }

        var nodes = new Dictionary<string, PackageAssemblyStep>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in definitions.Values)
        {
            Add(package, "runtime", package.Document.Runtime);
            if (includeEditor) Add(package, "editor", package.Document.Editor);
        }

        var predecessors = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in nodes.Keys)
        {
            predecessors.Add(key, new(StringComparer.OrdinalIgnoreCase));
            dependents.Add(key, []);
        }
        foreach (var (key, node) in nodes)
        {
            if (node.Target == "editor" && nodes.ContainsKey(Key(node.Package.Document.Id, "runtime")))
                Require(key, Key(node.Package.Document.Id, "runtime"));
            foreach (var dependency in node.Assembly.Dependencies)
            {
                if (dependency.Target is not ("runtime" or "editor") ||
                    node.Target == "runtime" && dependency.Target == "editor")
                    throw new InvalidDataException($"Invalid assembly dependency from '{node.Package.Document.Id}/{node.Target}' to '{dependency.PackageId}/{dependency.Target}'.");
                var targetKey = Key(dependency.PackageId, dependency.Target);
                if (nodes.ContainsKey(targetKey)) Require(key, targetKey);
                else if (!dependency.Optional)
                    throw new InvalidDataException($"Missing dependency '{dependency.PackageId}/{dependency.Target}' required by '{node.Package.Document.Id}/{node.Target}'.");
            }
        }

        var ready = new SortedSet<string>(Comparer<string>.Create((a, b) =>
        {
            var phase = string.CompareOrdinal(nodes[a].Target, nodes[b].Target);
            // Runtime assemblies always precede Editor assemblies.
            return phase != 0 ? -phase : StringComparer.OrdinalIgnoreCase.Compare(a, b);
        }));
        foreach (var (key, dependencies) in predecessors)
            if (dependencies.Count == 0) ready.Add(key);
        var ordered = new List<PackageAssemblyStep>(nodes.Count);
        while (ready.Count > 0)
        {
            var key = ready.Min!;
            ready.Remove(key);
            ordered.Add(nodes[key]);
            foreach (var dependent in dependents[key])
                if (predecessors[dependent].Remove(key) && predecessors[dependent].Count == 0)
                    ready.Add(dependent);
        }
        if (ordered.Count != nodes.Count)
            throw new InvalidDataException("Cyclic package assembly dependencies: " +
                string.Join(", ", predecessors.Where(static entry => entry.Value.Count > 0)
                    .Select(static entry => entry.Key.Replace('\0', '/')).Order(StringComparer.OrdinalIgnoreCase)));
        return ordered.AsReadOnly();

        void Add(BPackageDefinition package, string target, PackageAssemblyDocument? assembly)
        {
            if (assembly is not null) nodes.Add(Key(package.Document.Id, target), new(package, target, assembly));
        }
        void Require(string dependent, string dependency)
        {
            if (predecessors[dependent].Add(dependency)) dependents[dependency].Add(dependent);
        }
    }

    private static string Key(string id, string target) => id + "\0" + target;
}
