namespace BEngine.Launcher;

internal sealed class PackageDependencyIndex
{
    internal LauncherPackageGraph Graph { get; }
    internal IReadOnlyDictionary<string, LauncherPackageNode> Nodes { get; }
    private readonly Dictionary<string, LauncherPackageEdge[]> _dependencies, _dependents;

    internal PackageDependencyIndex(LauncherPackageGraph graph, string kind = "All")
    {
        Nodes = BuildIndex401(graph.Nodes, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageNode> BuildIndex401(global::System.Collections.Generic.IEnumerable<global::BEngine.Launcher.LauncherPackageNode> indexSource401, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer401)
        {
            ArgumentNullException.ThrowIfNull(indexSource401, "source");
            var knownCount = indexSource401.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageNode>(indexComparer401);
            global::System.ReadOnlySpan<global::BEngine.Launcher.LauncherPackageNode> values;
            if (knownCount && indexSource401 is global::BEngine.Launcher.LauncherPackageNode[] array)
                values = array;
            else if (knownCount && indexSource401 is global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageNode> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageNode>(capacity, indexComparer401);
                foreach (var item in indexSource401)
                    result.Add(IndexKey401(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageNode>(values.Length, indexComparer401);
                foreach (var item in values)
                    result.Add(IndexKey401(item), item);
                return result;
            }
        
            string IndexKey401(global::BEngine.Launcher.LauncherPackageNode node) => node.Id;
        }
        var edges = kind == "All" ? graph.Edges : graph.Edges.Select(edge =>
        {
            var declarations = edge.Declarations.Where(item => item.Source == kind).ToArray();
            var target = Nodes[edge.Target].Package;
            bool Query805()
            {
                var querySource805 = declarations;
                ArgumentNullException.ThrowIfNull(querySource805, "source");
                if (querySource805.GetType() == typeof(global::BEngine.Launcher.LauncherPackageDependency[]) || querySource805.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageDependency>))
                {
                    global::System.ReadOnlySpan<global::BEngine.Launcher.LauncherPackageDependency> queryValues805 = querySource805.GetType() == typeof(global::BEngine.Launcher.LauncherPackageDependency[]) ? (global::BEngine.Launcher.LauncherPackageDependency[])(object)querySource805 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageDependency>)(object)querySource805);
                    foreach (var item in queryValues805)
                    {
                        if (item.Target == "runtime" ? !target.HasRuntime : !target.HasEditor)
                            return true;
                    }
                }
                else
                {
                    foreach (var item in querySource805)
                    {
                        if (item.Target == "runtime" ? !target.HasRuntime : !target.HasEditor)
                            return true;
                    }
                }
            
                return false;
            }
            
            return edge with
            {
                Declarations = declarations,
                Unavailable = target is null || Query805()
            };
        }).Where(edge => edge.Declarations.Count != 0).ToArray();
        Graph = kind == "All" ? graph : new LauncherPackageGraph(graph.Nodes, edges);
        LauncherPackageEdge[] Sort(IEnumerable<LauncherPackageEdge> items, bool incoming) => items
            .OrderBy(edge => Nodes[incoming ? edge.Source : edge.Target].Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => incoming ? edge.Source : edge.Target, StringComparer.OrdinalIgnoreCase).ToArray();
        _dependencies = BuildIndex2893(edges.GroupBy(edge => edge.Source, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageEdge[]> BuildIndex2893(global::System.Collections.Generic.IEnumerable<global::System.Linq.IGrouping<string, global::BEngine.Launcher.LauncherPackageEdge>> indexSource2893, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer2893)
        {
            ArgumentNullException.ThrowIfNull(indexSource2893, "source");
            var knownCount = indexSource2893.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageEdge[]>(indexComparer2893);
            global::System.ReadOnlySpan<global::System.Linq.IGrouping<string, global::BEngine.Launcher.LauncherPackageEdge>> values;
            if (knownCount && indexSource2893 is global::System.Linq.IGrouping<string, global::BEngine.Launcher.LauncherPackageEdge>[] array)
                values = array;
            else if (knownCount && indexSource2893 is global::System.Collections.Generic.List<global::System.Linq.IGrouping<string, global::BEngine.Launcher.LauncherPackageEdge>> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageEdge[]>(capacity, indexComparer2893);
                foreach (var item in indexSource2893)
                    result.Add(IndexKey2893(item), IndexValue2893(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageEdge[]>(values.Length, indexComparer2893);
                foreach (var item in values)
                    result.Add(IndexKey2893(item), IndexValue2893(item));
                return result;
            }
        
            string IndexKey2893(global::System.Linq.IGrouping<string, global::BEngine.Launcher.LauncherPackageEdge> group) => group.Key;
            global::BEngine.Launcher.LauncherPackageEdge[] IndexValue2893(global::System.Linq.IGrouping<string, global::BEngine.Launcher.LauncherPackageEdge> group) => Sort(group, false);
        }
        _dependents = BuildIndex3094(edges.GroupBy(edge => edge.Target, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageEdge[]> BuildIndex3094(global::System.Collections.Generic.IEnumerable<global::System.Linq.IGrouping<string, global::BEngine.Launcher.LauncherPackageEdge>> indexSource3094, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer3094)
        {
            ArgumentNullException.ThrowIfNull(indexSource3094, "source");
            var knownCount = indexSource3094.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageEdge[]>(indexComparer3094);
            global::System.ReadOnlySpan<global::System.Linq.IGrouping<string, global::BEngine.Launcher.LauncherPackageEdge>> values;
            if (knownCount && indexSource3094 is global::System.Linq.IGrouping<string, global::BEngine.Launcher.LauncherPackageEdge>[] array)
                values = array;
            else if (knownCount && indexSource3094 is global::System.Collections.Generic.List<global::System.Linq.IGrouping<string, global::BEngine.Launcher.LauncherPackageEdge>> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageEdge[]>(capacity, indexComparer3094);
                foreach (var item in indexSource3094)
                    result.Add(IndexKey3094(item), IndexValue3094(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageEdge[]>(values.Length, indexComparer3094);
                foreach (var item in values)
                    result.Add(IndexKey3094(item), IndexValue3094(item));
                return result;
            }
        
            string IndexKey3094(global::System.Linq.IGrouping<string, global::BEngine.Launcher.LauncherPackageEdge> group) => group.Key;
            global::BEngine.Launcher.LauncherPackageEdge[] IndexValue3094(global::System.Linq.IGrouping<string, global::BEngine.Launcher.LauncherPackageEdge> group) => Sort(group, true);
        }
    }

    internal IReadOnlyList<LauncherPackageEdge> Adjacent(string id, bool incoming) =>
        (incoming ? _dependents : _dependencies).TryGetValue(id, out var edges) ? edges : [];

    internal string Describe(LauncherPackageEdge edge, HubLocalization language)
    {
        var relation = $"{Nodes[edge.Source].Title} → {Nodes[edge.Target].Title}";
        return string.Join(Environment.NewLine, edge.Declarations.Select(item =>
            $"{relation} [{language.Text(item.Source)} → {language.Text(item.Target)}]" +
            (item.Optional ? " · " + language.Text("Optional") : string.Empty))) +
            (edge.Unavailable ? Environment.NewLine + language.Text("Unavailable dependency") : string.Empty);
    }
}
