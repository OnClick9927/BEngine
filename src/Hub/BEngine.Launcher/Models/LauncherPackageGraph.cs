namespace BEngine.Launcher;

internal sealed record LauncherPackageNode(string Id, LauncherPackageInfo? Package, int Column, bool InCycle)
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
        var catalog = packages.ToDictionary(package => package.Id, IdComparer);
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

        // Collapse strongly connected groups before laying out the dependency DAG.
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
            foreach (var edge in outgoing[id])
            {
                if (!indices.ContainsKey(edge.Target))
                {
                    Visit(edge.Target);
                    low[id] = Math.Min(low[id], low[edge.Target]);
                }
                else if (active.Contains(edge.Target)) low[id] = Math.Min(low[id], indices[edge.Target]);
            }
            if (low[id] != indices[id]) return;
            var members = new List<string>();
            string member;
            do { member = stack.Pop(); active.Remove(member); members.Add(member); }
            while (!IdComparer.Equals(member, id));
            components.Add(members);
        }
        foreach (var id in included.Order(IdComparer)) if (!indices.ContainsKey(id)) Visit(id);
        var componentById = new Dictionary<string, int>(IdComparer);
        for (var index = 0; index < components.Count; index++)
            foreach (var id in components[index]) componentById[id] = index;
        var links = edges.Select(edge => (From: componentById[edge.Source], To: componentById[edge.Target]))
            .Where(link => link.From != link.To).Distinct().ToArray();
        var incoming = new int[components.Count];
        var columns = new int[components.Count];
        foreach (var link in links) incoming[link.To]++;
        var queue = new Queue<int>(Enumerable.Range(0, components.Count).Where(index => incoming[index] == 0));
        var targets = links.ToLookup(link => link.From, link => link.To);
        while (queue.TryDequeue(out var from))
            foreach (var to in targets[from])
            {
                columns[to] = Math.Max(columns[to], columns[from] + 1);
                if (--incoming[to] == 0) queue.Enqueue(to);
            }
        return new LauncherPackageGraph(included.Select(id => new LauncherPackageNode(id,
                catalog.GetValueOrDefault(id), columns[componentById[id]], components[componentById[id]].Count > 1))
            .OrderBy(node => node.Column).ThenBy(node => node.Title, IdComparer).ThenBy(node => node.Id, IdComparer).ToArray(), edges);
    }
}
