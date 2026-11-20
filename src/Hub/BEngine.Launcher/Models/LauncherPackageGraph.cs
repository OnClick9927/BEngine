namespace BEngine.Launcher;

internal sealed record LauncherPackageNode(string Id, LauncherPackageInfo? Package, bool InCycle)
{
    internal string Title => Package?.DisplayName ?? Id;
}

internal sealed record LauncherPackageEdge(string Source, string Target,
    IReadOnlyList<LauncherPackageDependency> Declarations, bool Unavailable);

internal sealed record LauncherPackageGraph(IReadOnlyList<LauncherPackageNode> Nodes,
    IReadOnlyList<LauncherPackageEdge> Edges)
{
    internal static readonly LauncherPackageGraph Empty = new([], []);
    private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;

    internal static LauncherPackageGraph Create(IReadOnlyList<LauncherPackageInfo> packages,
        IEnumerable<string> roots, string assembly = "All")
    {
        var catalog = BuildIndex821(packages, IdComparer);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageInfo> BuildIndex821(global::System.Collections.Generic.IEnumerable<global::BEngine.Launcher.LauncherPackageInfo> indexSource821, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer821)
        {
            ArgumentNullException.ThrowIfNull(indexSource821, "source");
            var knownCount = indexSource821.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageInfo>(indexComparer821);
            global::System.ReadOnlySpan<global::BEngine.Launcher.LauncherPackageInfo> values;
            if (knownCount && indexSource821 is global::BEngine.Launcher.LauncherPackageInfo[] array)
                values = array;
            else if (knownCount && indexSource821 is global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageInfo> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageInfo>(capacity, indexComparer821);
                foreach (var item in indexSource821)
                    result.Add(IndexKey821(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageInfo>(values.Length, indexComparer821);
                foreach (var item in values)
                    result.Add(IndexKey821(item), item);
                return result;
            }
        
            string IndexKey821(global::BEngine.Launcher.LauncherPackageInfo package) => package.Id;
        }
        string CanonicalId(string id) => catalog.TryGetValue(id, out var package) ? package.Id : id.ToLowerInvariant();
        var allEdges = packages.SelectMany(package => package.Dependencies
                .Where(dependency => (assembly == "All" || dependency.Source == assembly) &&
                    !IdComparer.Equals(package.Id, dependency.PackageId))
                .Select(dependency => (Source: package.Id, Target: CanonicalId(dependency.PackageId), Dependency: dependency)))
            .GroupBy(item => (item.Source, item.Target))
            .Select(group => new LauncherPackageEdge(group.Key.Source, group.Key.Target,
                group.Select(item => item.Dependency).Distinct().ToArray(),
                !catalog.TryGetValue(group.Key.Target, out var target) || group.Any(item =>
                    item.Dependency.Target == "runtime" ? !target.HasRuntime : !target.HasEditor)))
            .OrderBy(edge => edge.Source, IdComparer).ThenBy(edge => edge.Target, IdComparer).ToArray();
        var outgoing = allEdges.ToLookup(edge => edge.Source, IdComparer);
        var included = new HashSet<string>(IdComparer);
        var pending = new Stack<string>(roots.Select(CanonicalId));
        while (pending.TryPop(out var id))
        {
            if (!included.Add(id)) continue;
            foreach (var edge in outgoing[id]) pending.Push(edge.Target);
        }
        var edges = allEdges.Where(edge => included.Contains(edge.Source)).ToArray();
        var assemblyCycles = FindAssemblyCyclePackages(catalog);

        return new LauncherPackageGraph(included.Select(id => new LauncherPackageNode(id,
                catalog.GetValueOrDefault(id), assemblyCycles.Contains(id)))
            .OrderBy(node => node.Title, IdComparer).ThenBy(node => node.Id, IdComparer).ToArray(), edges);
    }
    private static HashSet<string> FindAssemblyCyclePackages(IReadOnlyDictionary<string, LauncherPackageInfo> catalog)
    {
        static string Key(string id, string kind) => id + "\0" + kind;
        var owners = new Dictionary<string, string>(IdComparer);
        var dependencies = new Dictionary<string, HashSet<string>>(IdComparer);
        foreach (var package in catalog.Values)
            foreach (var kind in new[] { "runtime", "editor" })
            {
                if (kind == "runtime" ? !package.HasRuntime : !package.HasEditor) continue;
                var key = Key(package.Id, kind);
                owners.Add(key, package.Id);
                dependencies.Add(key, new HashSet<string>(IdComparer));
            }
        foreach (var package in catalog.Values)
        {
            if (package.HasEditor && package.HasRuntime)
                dependencies[Key(package.Id, "editor")].Add(Key(package.Id, "runtime"));
            foreach (var declaration in package.Dependencies)
            {
                var source = Key(package.Id, declaration.Source);
                var target = Key(declaration.PackageId, declaration.Target);
                // Missing targets have their own diagnostic and cannot form a cycle.
                if (dependencies.TryGetValue(source, out var targets) && dependencies.ContainsKey(target))
                    targets.Add(target);
            }
        }
        var cycles = new HashSet<string>(IdComparer);
        foreach (var component in FindComponents(dependencies.Keys, id => dependencies[id]))
            if (component.Count > 1 || dependencies[component[0]].Contains(component[0]))
                foreach (var id in component) cycles.Add(owners[id]);
        return cycles;
    }

    internal static List<List<string>> FindComponents(IEnumerable<string> nodes, Func<string, IEnumerable<string>> dependencies)
    {
        var indices = new Dictionary<string, int>(IdComparer);
        var low = new Dictionary<string, int>(IdComparer);
        var stack = new Stack<string>();
        var active = new HashSet<string>(IdComparer);
        var components = new List<List<string>>();
        void Visit(string id)
        {
            indices[id] = low[id] = indices.Count;
            stack.Push(id);
            active.Add(id);
            foreach (var target in dependencies(id))
            {
                if (!indices.ContainsKey(target))
                {
                    Visit(target);
                    low[id] = Math.Min(low[id], low[target]);
                }
                else if (active.Contains(target)) low[id] = Math.Min(low[id], indices[target]);
            }
            if (low[id] != indices[id]) return;
            var members = new List<string>();
            string member;
            do { member = stack.Pop(); active.Remove(member); members.Add(member); }
            while (!IdComparer.Equals(member, id));
            components.Add(members);
        }
        foreach (var id in nodes.Order(IdComparer)) if (!indices.ContainsKey(id)) Visit(id);
        return components;
    }
}
