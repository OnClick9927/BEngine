using System.Drawing.Drawing2D;

namespace BEngine.Launcher;

internal sealed record PackageGraphEdgeRoute(int Edge, GraphicsPath Path, RectangleF Bounds, PointF Source, PointF Target, float? TopLane, bool Optional);

// Original overview: unique nodes, dependency layers and separate overhead lanes.
internal sealed class PackageGraphLayout : IDisposable
{
    internal Dictionary<string, RectangleF> Nodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, int> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal List<PackageGraphEdgeRoute> Routes { get; } = [];
    internal SizeF ContentSize { get; private set; }
    internal static PackageGraphLayout Build(LauncherPackageGraph graph, IReadOnlyDictionary<string, SizeF> sizes, float scale)
    {
        var result = new PackageGraphLayout();
        try { result.Arrange(graph, sizes, scale); return result; }
        catch { result.Dispose(); throw; }
    }
    private void Arrange(LauncherPackageGraph graph, IReadOnlyDictionary<string, SizeF> sizes, float scale)
    {
        if (graph.Nodes.Count == 0) return;
        var outgoing = graph.Edges.Select((edge, index) => (edge, index)).ToLookup(item => item.edge.Source, StringComparer.OrdinalIgnoreCase);
        var incoming = graph.Edges.Select((edge, index) => (edge, index)).ToLookup(item => item.edge.Target, StringComparer.OrdinalIgnoreCase);
        // Visual package cycles share a layer; assembly-cycle diagnostics remain unchanged.
        var components = LauncherPackageGraph.FindComponents(graph.Nodes.Select(node => node.Id), id => outgoing[id].Select(item => item.edge.Target));
        var owners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < components.Count; i++) foreach (var id in components[i]) owners.Add(id, i);
        var links = (((global::System.Collections.Generic.IEnumerable<(int From, int To)>)(graph.Edges.Select(edge => (From: owners[edge.Source], To: owners[edge.Target])).Where(link => link.From != link.To))) switch
        {
            (int From, int To)[] { Length: 0 } => global::System.Array.Empty<(int From, int To)>(),
            var distinctItems1866 => distinctItems1866.ToHashSet<(int From, int To)>().ToArray()});
        var indegrees = new int[components.Count]; var layers = new int[components.Count];
        foreach (var link in links) indegrees[link.To]++;
        var pending = new Queue<int>(Enumerable.Range(0, components.Count).Where(i => indegrees[i] == 0));
        var targets = links.ToLookup(link => link.From, link => link.To);
        while (pending.TryDequeue(out var from)) foreach (var to in targets[from])
        { layers[to] = Math.Max(layers[to], layers[from] + 1); if (--indegrees[to] == 0) pending.Enqueue(to); }
        foreach (var node in graph.Nodes) Columns.Add(node.Id, layers[owners[node.Id]]);
        var lanes = new Dictionary<int, float>();
        for (var i = 0; i < graph.Edges.Count; i++)
        {
            var edge = graph.Edges[i];
            if (Columns[edge.Target] - Columns[edge.Source] > 1) lanes.Add(i, (16 + lanes.Count * 10) * scale);
        }
        var top = Math.Max(40, 26 + lanes.Count * 10) * scale;
        var linked = graph.Edges.SelectMany(edge => new[] { edge.Source, edge.Target }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var columns = graph.Nodes.Where(node => linked.Contains(node.Id)).GroupBy(node => Columns[node.Id]).OrderBy(group => group.Key).ToArray();
        var rights = new Dictionary<int, float>(); var left = 20 * scale;
        foreach (var column in columns)
        {
            var y = top; var width = column.Max(node => sizes[node.Id].Width);
            foreach (var node in column.OrderBy(node => node.Title, StringComparer.OrdinalIgnoreCase).ThenBy(node => node.Id, StringComparer.OrdinalIgnoreCase))
            { Nodes.Add(node.Id, new RectangleF(new PointF(left, y), sizes[node.Id])); y += sizes[node.Id].Height + 14 * scale; }
            rights.Add(column.Key, left + width); left += width + 64 * scale;
        }
        var rows = columns.Length == 0 ? Math.Max(1, (int)Math.Ceiling(Math.Sqrt(graph.Nodes.Count))) : columns.Max(group => group.Count());
        foreach (var group in graph.Nodes.Where(node => !linked.Contains(node.Id)).Chunk(rows))
        {
            var y = top;
            foreach (var node in group) { Nodes.Add(node.Id, new RectangleF(new PointF(left, y), sizes[node.Id])); y += sizes[node.Id].Height + 14 * scale; }
            left += group.Max(node => sizes[node.Id].Width) + 22 * scale;
        }
        foreach (var group in outgoing)
        {
            var sourceEdges = group.ToArray();
            for (var sourceIndex = 0; sourceIndex < sourceEdges.Length; sourceIndex++)
            {
                var (edge, index) = sourceEdges[sourceIndex]; var targetEdges = incoming[edge.Target].ToArray();
                var targetIndex = Array.FindIndex(targetEdges, item => item.index == index);
                var from = Nodes[edge.Source]; var to = Nodes[edge.Target];
                var start = new PointF(from.Right, from.Top + from.Height * (sourceIndex + 1) / (sourceEdges.Length + 1));
                var end = new PointF(to.Left, to.Top + to.Height * (targetIndex + 1) / (targetEdges.Length + 1));
                var path = new GraphicsPath();
                try
                {
                    if (Columns[edge.Source] == Columns[edge.Target])
                    {
                        end.X = to.Right;
                        var exit = new PointF(rights[Columns[edge.Source]] + 8 * scale, start.Y); var entry = new PointF(exit.X, end.Y);
                        var turn = rights[Columns[edge.Source]] + 48 * scale;
                        path.AddLine(start, exit); path.AddBezier(exit, new PointF(turn, start.Y), new PointF(turn, end.Y), entry); path.AddLine(entry, end);
                    }
                    else if (lanes.TryGetValue(index, out var lane))
                    {
                        var exit = rights[Columns[edge.Source]] + (12 + index % 5 * 7) * scale;
                        var entry = end.X - (12 + targetIndex % 5 * 7) * scale;
                        path.AddLines([start, new PointF(exit, start.Y), new PointF(exit, lane), new PointF(entry, lane), new PointF(entry, end.Y), end]);
                    }
                    else
                    {
                        var exit = new PointF(rights[Columns[edge.Source]] + 8 * scale, start.Y); var gap = (end.X - exit.X) / 2;
                        path.AddLine(start, exit); path.AddBezier(exit, new PointF(exit.X + gap, start.Y), new PointF(end.X - gap, end.Y), end);
                    }
                    Routes.Add(new PackageGraphEdgeRoute(index, path, path.GetBounds(), start, end, lanes.TryGetValue(index, out var topLane) ? topLane : null, Scan6634(edge.Declarations)));
                    [global::BEngine.SkipLoopGuard]
                    bool Scan6634(global::System.Collections.Generic.IEnumerable<global::BEngine.Launcher.LauncherPackageDependency> scanSource6634)
                    {
                        ArgumentNullException.ThrowIfNull(scanSource6634, "source");
                        global::System.ReadOnlySpan<global::BEngine.Launcher.LauncherPackageDependency> scanValues6634;
                        if (scanSource6634.GetType() == typeof(global::BEngine.Launcher.LauncherPackageDependency[]))
                            scanValues6634 = (global::BEngine.Launcher.LauncherPackageDependency[])scanSource6634;
                        else if (scanSource6634.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageDependency>))
                            scanValues6634 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageDependency>)scanSource6634);
                        else
                        {
                            foreach (var scanItem6634 in scanSource6634)
                            {
                                if (!Match6634(scanItem6634))
                                    return false;
                            }
                    
                            return true;
                        }
                    
                        foreach (var scanItem6634 in scanValues6634)
                        {
                            if (!Match6634(scanItem6634))
                                return false;
                        }
                    
                        return true;
                        bool Match6634(global::BEngine.Launcher.LauncherPackageDependency declaration) => declaration.Optional;
                    }
                }
                catch { path.Dispose(); throw; }
            }
        }
        Routes.Sort((a, b) => a.Edge.CompareTo(b.Edge));
        ContentSize = new SizeF(Nodes.Values.Max(rect => rect.Right) + 56 * scale, Nodes.Values.Max(rect => rect.Bottom) + 20 * scale);
    }
    public void Dispose() { foreach (var route in Routes) route.Path.Dispose(); Routes.Clear(); }
}
