using System.Drawing.Drawing2D;
using DrawingColor = System.Drawing.Color;

namespace BEngine.Launcher;

internal readonly record struct PackageGraphViewState(float Zoom, PointF Offset, bool FitOnResize, Size Viewport, string? SelectedId, LauncherPackageGraph Graph);

internal sealed partial class PackageDependencyGraphControl : Control
{
    private Dictionary<string, RectangleF> _bounds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ToolTip _tooltip = new() { AutoPopDelay = 20000 };
    private readonly System.Drawing.Font _titleFont = new("Segoe UI", 11, FontStyle.Bold), _badgeFont = new("Segoe UI", 8.5f, FontStyle.Bold);
    private readonly StringFormat _titleFormat = new(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.None };
    private LauncherPalette _palette = LauncherPalette.From(new());
    private HubLocalization _language = new("system");
    private PackageGraphLayout? _layout;
    private Dictionary<string, LauncherPackageNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dependencies = new(StringComparer.OrdinalIgnoreCase), _dependents = new(StringComparer.OrdinalIgnoreCase);
    private byte[] _edgeKinds = [];
    private SizeF _contentSize, _requiredBadgeSize;
    private float _titleHeight;
    private PointF _offset, _panStart;
    private Point _mouseDown;
    private bool _dragging, _doubleClick, _fitOnResize = true;
    private string? _hovered;
    private PackageGraphEdgeRoute? _hoveredRoute;
    private string _emptyMessage = "No packages match the current type and search";
    internal LauncherPackageGraph Graph { get; private set; } = LauncherPackageGraph.Empty;
    internal string? SelectedId { get; private set; }
    internal float Zoom { get; private set; } = 1;
    internal int LayoutRevision { get; private set; }
    internal event Action<string?>? PackageSelected;
    internal event Action<string>? PackageActivated;
    internal PackageDependencyGraphControl()
    {
        DoubleBuffered = ResizeRedraw = TabStop = true;
        AccessibleRole = AccessibleRole.Diagram; _routePen.CustomEndCap = _arrowCap;
        SetStyle(ControlStyles.Selectable, true);
    }
    internal void SetAppearance(LauncherPalette palette, HubLocalization language)
    {
        _palette = palette; _language = language; BackColor = palette.Panel; ForeColor = palette.Primary;
        AccessibleName = language.Text("Package dependency graph"); ClearHover(); UpdateAccessibleDescription(); Invalidate();
    }
    internal void SetGraph(LauncherPackageGraph graph, string? selectedId, string emptyMessage = "No packages match the current type and search")
    {
        var changed = _layout is null || !SameGeometry(Graph, graph);
        Graph = graph; _nodes = BuildIndex2822(graph.Nodes, StringComparer.OrdinalIgnoreCase);
                       [global::BEngine.SkipLoopGuard]
                       global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageNode> BuildIndex2822(global::System.Collections.Generic.IEnumerable<global::BEngine.Launcher.LauncherPackageNode> indexSource2822, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer2822)
                       {
                           ArgumentNullException.ThrowIfNull(indexSource2822, "source");
                           var knownCount = indexSource2822.TryGetNonEnumeratedCount(out var capacity);
                           if (knownCount && capacity == 0)
                               return new global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageNode>(indexComparer2822);
                           global::System.ReadOnlySpan<global::BEngine.Launcher.LauncherPackageNode> values;
                           if (knownCount && indexSource2822 is global::BEngine.Launcher.LauncherPackageNode[] array)
                               values = array;
                           else if (knownCount && indexSource2822 is global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageNode> list)
                               values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
                           else
                           {
                               var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageNode>(capacity, indexComparer2822);
                               foreach (var item in indexSource2822)
                                   result.Add(IndexKey2822(item), item);
                               return result;
                           }
                       
                           {
                               var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Launcher.LauncherPackageNode>(values.Length, indexComparer2822);
                               foreach (var item in values)
                                   result.Add(IndexKey2822(item), item);
                               return result;
                           }
                       
                           string IndexKey2822(global::BEngine.Launcher.LauncherPackageNode node) => node.Id;
                       }
        SelectedId = _nodes.TryGetValue(selectedId ?? string.Empty, out var selected) ? selected.Id : null;
        _emptyMessage = emptyMessage; ClearHover(); UpdateAccessibleDescription();
        if (changed) { LayoutNodes(); if (_fitOnResize) Fit(); } else UpdateHighlights();
        Invalidate();
    }
    private static bool SameGeometry(LauncherPackageGraph a, LauncherPackageGraph b)
    {
        if (a.Nodes.Count != b.Nodes.Count || a.Edges.Count != b.Edges.Count) return false;
        for (var i = 0; i < a.Nodes.Count; i++)
        {
            var x = a.Nodes[i]; var y = b.Nodes[i];
            if (x.Id != y.Id || x.Title != y.Title || x.InCycle != y.InCycle || (x.Package is null) != (y.Package is null) || x.Package?.IsRequired != y.Package?.IsRequired) return false;
        }
        for (var i = 0; i < a.Edges.Count; i++)
        {
            var x = a.Edges[i]; var y = b.Edges[i];
            if (x.Source != y.Source || x.Target != y.Target || x.Unavailable != y.Unavailable || Scan6323(x.Declarations) != Scan6362(y.Declarations))
                return false;
            [global::BEngine.SkipLoopGuard]
            bool Scan6362(global::System.Collections.Generic.IEnumerable<global::BEngine.Launcher.LauncherPackageDependency> scanSource6362)
            {
                ArgumentNullException.ThrowIfNull(scanSource6362, "source");
                global::System.ReadOnlySpan<global::BEngine.Launcher.LauncherPackageDependency> scanValues6362;
                if (scanSource6362.GetType() == typeof(global::BEngine.Launcher.LauncherPackageDependency[]))
                    scanValues6362 = (global::BEngine.Launcher.LauncherPackageDependency[])scanSource6362;
                else if (scanSource6362.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageDependency>))
                    scanValues6362 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageDependency>)scanSource6362);
                else
                {
                    foreach (var scanItem6362 in scanSource6362)
                    {
                        if (!Match6362(scanItem6362))
                            return false;
                    }
            
                    return true;
                }
            
                foreach (var scanItem6362 in scanValues6362)
                {
                    if (!Match6362(scanItem6362))
                        return false;
                }
            
                return true;
                bool Match6362(global::BEngine.Launcher.LauncherPackageDependency d) => d.Optional;
            }
            
            [global::BEngine.SkipLoopGuard]
            bool Scan6323(global::System.Collections.Generic.IEnumerable<global::BEngine.Launcher.LauncherPackageDependency> scanSource6323)
            {
                ArgumentNullException.ThrowIfNull(scanSource6323, "source");
                global::System.ReadOnlySpan<global::BEngine.Launcher.LauncherPackageDependency> scanValues6323;
                if (scanSource6323.GetType() == typeof(global::BEngine.Launcher.LauncherPackageDependency[]))
                    scanValues6323 = (global::BEngine.Launcher.LauncherPackageDependency[])scanSource6323;
                else if (scanSource6323.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageDependency>))
                    scanValues6323 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageDependency>)scanSource6323);
                else
                {
                    foreach (var scanItem6323 in scanSource6323)
                    {
                        if (!Match6323(scanItem6323))
                            return false;
                    }
            
                    return true;
                }
            
                foreach (var scanItem6323 in scanValues6323)
                {
                    if (!Match6323(scanItem6323))
                        return false;
                }
            
                return true;
                bool Match6323(global::BEngine.Launcher.LauncherPackageDependency d) => d.Optional;
            }
        }
        return true;
    }
    private void LayoutNodes()
    {
        using var graphics = CreateGraphics(); var scale = DeviceDpi / 96f;
        _titleHeight = MathF.Ceiling(graphics.MeasureString("Ag", _titleFont, int.MaxValue, _titleFormat).Height);
        // Both translations share a fixed reservation, so language changes preserve layout.
        var badgeWidth = new[] { "Required", "必需" }.Max(text => graphics.MeasureString(text, _badgeFont, int.MaxValue, _titleFormat).Width);
        _requiredBadgeSize = new SizeF(MathF.Ceiling(badgeWidth) + 30 * scale,
            MathF.Ceiling(graphics.MeasureString("Ag", _badgeFont, int.MaxValue, _titleFormat).Height) + 6 * scale);
        var sizes = new Dictionary<string, SizeF>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in Graph.Nodes)
        {
            var title = graphics.MeasureString(node.Title, _titleFont, int.MaxValue, _titleFormat);
            var width = MathF.Ceiling(title.Width) + (node.Package is null || node.InCycle ? 40 : 24) * scale;
            var height = MathF.Ceiling(title.Height) + 16 * scale;
            if (node.Package?.IsRequired == true) { width = Math.Max(width, _requiredBadgeSize.Width + 20 * scale); height += _requiredBadgeSize.Height + 5 * scale; }
            sizes.Add(node.Id, new SizeF(width, height));
        }
        var next = PackageGraphLayout.Build(Graph, sizes, scale);
        _layout?.Dispose(); _layout = next; _bounds = next.Nodes; _contentSize = next.ContentSize;
        _edgeKinds = new byte[Graph.Edges.Count]; LayoutRevision++; UpdateHighlights();
    }
    private void UpdateHighlights()
    {
        _dependencies.Clear(); _dependents.Clear(); Array.Clear(_edgeKinds);
        var focus = SelectedId ?? _hovered;
        for (var i = 0; i < Graph.Edges.Count && i < _edgeKinds.Length; i++)
        {
            var edge = Graph.Edges[i];
            if (edge.Source == focus) { _edgeKinds[i] = 1; _dependencies.Add(edge.Target); }
            if (edge.Target == focus) { _edgeKinds[i] = 2; _dependents.Add(edge.Source); }
        }
        if (focus is null && _hoveredRoute is { } route) _edgeKinds[route.Edge] = 1;
    }
    internal void SelectPackage(string id)
    { if (!_nodes.TryGetValue(id, out var node)) return; SelectedId = node.Id; ClearHover(); UpdateAccessibleDescription(); Invalidate(); PackageSelected?.Invoke(SelectedId); }
    internal void ClearSelection()
    { SelectedId = null; ClearHover(); UpdateAccessibleDescription(); Invalidate(); PackageSelected?.Invoke(null); }
    private void ClearHover() { _hovered = null; _hoveredRoute = null; _tooltip.SetToolTip(this, null); Cursor = Cursors.Default; UpdateHighlights(); }
    private void UpdateAccessibleDescription() => AccessibleDescription = _nodes.TryGetValue(SelectedId ?? string.Empty, out var node) ? Describe(node) : null;
    internal void Fit()
    {
        _fitOnResize = true;
        Zoom = _contentSize.IsEmpty ? 1 : Math.Clamp(Math.Min(Math.Max(1, ClientSize.Width - 20) / _contentSize.Width,
            Math.Max(1, ClientSize.Height - 20) / _contentSize.Height), .12f, 1.3f);
        _offset = new PointF((ClientSize.Width - _contentSize.Width * Zoom) / 2, (ClientSize.Height - _contentSize.Height * Zoom) / 2);
        if (SelectedId is { } id && _bounds.TryGetValue(id, out var selected) && (_contentSize.Width * Zoom > ClientSize.Width || _contentSize.Height * Zoom > ClientSize.Height))
            _offset = new PointF(ClientSize.Width / 2f - (selected.Left + selected.Width / 2) * Zoom, ClientSize.Height / 2f - (selected.Top + selected.Height / 2) * Zoom);
        Invalidate();
    }
    internal void ZoomBy(float factor) => ZoomAt(factor, new Point(ClientSize.Width / 2, ClientSize.Height / 2));
    internal PackageGraphViewState CaptureView() => new(Zoom, _offset, _fitOnResize, ClientSize, SelectedId, Graph);
    internal void RestoreView(PackageGraphViewState state)
    {
        SelectedId = _nodes.ContainsKey(state.SelectedId ?? string.Empty) ? state.SelectedId : null;
        ClearHover(); UpdateAccessibleDescription();
        if (!SameGeometry(Graph, state.Graph) || state.FitOnResize && state.Viewport != ClientSize) { Fit(); return; }
        Zoom = state.Zoom; _offset = new PointF(state.Offset.X + (ClientSize.Width - state.Viewport.Width) / 2f, state.Offset.Y + (ClientSize.Height - state.Viewport.Height) / 2f);
        _fitOnResize = state.FitOnResize; Invalidate();
    }
    private void ZoomAt(float factor, Point anchor)
    {
        var next = Math.Clamp(Zoom * factor, .12f, 2.5f);
        _offset = new PointF(anchor.X - (anchor.X - _offset.X) * next / Zoom, anchor.Y - (anchor.Y - _offset.Y) * next / Zoom);
        Zoom = next; _fitOnResize = false; Invalidate();
    }
    private PointF World(Point point) => new((point.X - _offset.X) / Zoom, (point.Y - _offset.Y) / Zoom);
    private string? Hit(Point point)
    { var world = World(point); foreach (var (id, bounds) in _bounds) if (bounds.Contains(world)) return id; return null; }
    private PackageGraphEdgeRoute? HitRoute(Point point)
    {
        if (_layout is null) return null;
        var world = World(point); _hitPen.Width = 8 / Zoom;
        foreach (var route in _layout.Routes)
            if (RectangleF.Inflate(route.Bounds, 4 / Zoom, 4 / Zoom).Contains(world) && route.Path.IsOutlineVisible(world, _hitPen)) return route;
        return null;
    }
    private string Describe(LauncherPackageNode node)
    {
        var lines = new List<string> { node.Title, node.Id };
        if (node.Package is not null)
        {
            lines.Add(_language.Text("Version {0}", node.Package.Version));
            lines.Add(_language.Text("Distribution: {0}", _language.Text(node.Package.DistributionLabel)));
        }
        if (node.Package?.IsRequired == true)
        {
            if (node.Package.Required) lines.Add(_language.Text("Declared as required by this engine"));
            var roots = node.Package.RequiredBy.Where(id => !string.Equals(node.Id, id, StringComparison.OrdinalIgnoreCase)).Select(id => _nodes.TryGetValue(id, out var root) ? root.Title : id).ToArray();
            if (roots.Length != 0) lines.Add(_language.Text("Required by: {0}", string.Join(", ", roots)));
        }
        if (node.Package is null) lines.Add(_language.Text("Missing package"));
        if (node.InCycle) lines.Add(_language.Text("Assembly dependency cycle"));
        {
            var __filteredSource10287 = Graph.Edges;
            ArgumentNullException.ThrowIfNull(__filteredSource10287, "source");
            foreach (var edge in __filteredSource10287)
            {
                if (!(edge.Source == node.Id))
                    continue;
                DescribeEdge(lines, edge);
            }
        }
        return string.Join(Environment.NewLine, lines);
    }
    private void DescribeEdge(List<string> lines, LauncherPackageEdge edge)
    {
        foreach (var declaration in edge.Declarations)
            lines.Add($"{_nodes[edge.Source].Title} → {_nodes[edge.Target].Title} [{_language.Text(declaration.Source)} → {_language.Text(declaration.Target)}]" +
                (declaration.Optional ? " · " + _language.Text("Optional") : string.Empty) + (edge.Unavailable ? " · " + _language.Text("Unavailable dependency") : string.Empty));
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); if (_fitOnResize) Fit(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); ClearHover(); LayoutNodes(); if (_fitOnResize) Fit(); else Invalidate(); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); ZoomAt(MathF.Pow(1.15f, e.Delta / 120f), e.Location); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); if (e.Button is not (MouseButtons.Left or MouseButtons.Middle)) return;
        if (e.Clicks == 1) _doubleClick = false;
        Focus(); _mouseDown = e.Location; _panStart = _offset; _dragging = false; Capture = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Capture)
        {
            var dx = e.X - _mouseDown.X; var dy = e.Y - _mouseDown.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 4) _dragging = true;
            if (_dragging) { _offset = new PointF(_panStart.X + dx, _panStart.Y + dy); _fitOnResize = false; Cursor = Cursors.SizeAll; Invalidate(); }
            return;
        }
        var id = Hit(e.Location); var route = id is null ? HitRoute(e.Location) : null;
        Cursor = id is null ? Cursors.Default : Cursors.Hand;
        if (_hovered == id && _hoveredRoute == route) return;
        _hovered = id; _hoveredRoute = route;
        string? description = id is not null ? Describe(_nodes[id]) : null;
        if (route is not null) { var lines = new List<string>(); DescribeEdge(lines, Graph.Edges[route.Edge]); description = string.Join(Environment.NewLine, lines); }
        _tooltip.SetToolTip(this, description); UpdateHighlights(); Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); ClearHover(); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); if (!Capture) return;
        Capture = false; Cursor = Cursors.Default;
        if (!_dragging && !_doubleClick && e.Button == MouseButtons.Left) { if (Hit(e.Location) is { } id) SelectPackage(id); else ClearSelection(); }
    }
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    { base.OnMouseDoubleClick(e); if (e.Button == MouseButtons.Left && Hit(e.Location) is { } id) { _doubleClick = true; PackageActivated?.Invoke(id); } }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Escape || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Home: Fit(); break;
            case Keys.Add: case Keys.Oemplus: ZoomBy(1.2f); break;
            case Keys.Subtract: case Keys.OemMinus: ZoomBy(1 / 1.2f); break;
            case Keys.Left: _offset.X += 40; _fitOnResize = false; break;
            case Keys.Right: _offset.X -= 40; _fitOnResize = false; break;
            case Keys.Up: _offset.Y += 40; _fitOnResize = false; break;
            case Keys.Down: _offset.Y -= 40; _fitOnResize = false; break;
            case Keys.Enter: if (SelectedId is { } id) PackageActivated?.Invoke(id); break;
            case Keys.Escape: ClearSelection(); break;
            default: return;
        }
        e.Handled = e.SuppressKeyPress = true; Invalidate();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _layout?.Dispose(); _layout = null; _tooltip.Dispose(); _titleFont.Dispose(); _badgeFont.Dispose(); _titleFormat.Dispose();
            _routePen.Dispose(); _haloPen.Dispose(); _hitPen.Dispose(); _outlinePen.Dispose(); _fillBrush.Dispose(); _textBrush.Dispose(); _arrowCap.Dispose();
        }
        base.Dispose(disposing);
    }
}
