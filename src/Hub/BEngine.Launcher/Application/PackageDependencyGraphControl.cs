using System.Drawing.Drawing2D;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;

namespace BEngine.Launcher;

internal sealed class PackageDependencyGraphControl : Control
{
    private readonly Dictionary<string, RectangleF> _bounds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _columns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, float> _columnRights = new();
    private readonly Dictionary<LauncherPackageEdge, float> _edgeLanes = new();
    private readonly ToolTip _tooltip = new() { AutoPopDelay = 20000 };
    private readonly DrawingFont _titleFont = new("Segoe UI", 11, FontStyle.Bold);
    private readonly StringFormat _titleFormat = new(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.None };
    private LauncherPalette _palette = LauncherPalette.From(new());
    private HubLocalization _language = new("system");
    private SizeF _contentSize;
    private PointF _offset;
    private Point _mouseDown;
    private PointF _panStart;
    private bool _dragging;
    private string? _hovered;
    private bool _fitOnResize = true;
    internal LauncherPackageGraph Graph { get; private set; } = LauncherPackageGraph.Empty;
    internal string? SelectedId { get; private set; }
    internal float Zoom { get; private set; } = 1;
    internal event Action<string>? PackageSelected;
    internal event Action<string>? PackageActivated;

    internal PackageDependencyGraphControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = true;
        AccessibleRole = AccessibleRole.Diagram;
        AccessibleName = "Package dependency graph";
        SetStyle(ControlStyles.Selectable, true);
    }

    internal void SetAppearance(LauncherPalette palette, HubLocalization language)
    {
        _palette = palette;
        _language = language;
        BackColor = palette.Panel;
        ForeColor = palette.Primary;
        AccessibleName = language.Text("Package dependency graph");
        _hovered = null;
        _tooltip.SetToolTip(this, null);
        Invalidate();
    }

    private string _emptyMessage = "No packages match the current type and search";

    internal void SetGraph(LauncherPackageGraph graph, string? selectedId,
        string emptyMessage = "No packages match the current type and search")
    {
        Graph = graph;
        SelectedId = selectedId;
        _emptyMessage = emptyMessage;
        LayoutNodes();
        _hovered = null;
        _tooltip.SetToolTip(this, null);
        Fit();
    }

    private void LayoutNodes()
    {
        _bounds.Clear();
        _columns.Clear();
        _columnRights.Clear();
        _edgeLanes.Clear();
        using var graphics = CreateGraphics();
        var sizes = new Dictionary<string, SizeF>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in Graph.Nodes)
        {
            var title = graphics.MeasureString(node.Title, _titleFont, int.MaxValue, _titleFormat);
            sizes.Add(node.Id, new SizeF(MathF.Ceiling(title.Width) + (node.Package is null || node.InCycle ? 40 : 24), MathF.Ceiling(title.Height) + 16));
            _columns.Add(node.Id, node.Column);
        }
        foreach (var edge in Graph.Edges.Where(edge => _columns[edge.Target] - _columns[edge.Source] > 1))
            _edgeLanes.Add(edge, 16 + _edgeLanes.Count * 10);
        var top = Math.Max(40, 26 + _edgeLanes.Count * 10);
        var linked = Graph.Edges.SelectMany(edge => new[] { edge.Source, edge.Target }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var columns = Graph.Nodes.Where(node => linked.Contains(node.Id)).GroupBy(node => node.Column).OrderBy(column => column.Key).ToArray();
        var left = 20f;
        foreach (var column in columns)
        {
            var y = (float)top;
            var width = column.Max(node => sizes[node.Id].Width);
            foreach (var node in column)
            {
                _bounds.Add(node.Id, new RectangleF(new PointF(left, y), sizes[node.Id]));
                y += sizes[node.Id].Height + 14;
            }
            _columnRights.Add(column.Key, left + width);
            left += width + 64;
        }
        // Independent packages use measured column widths too; no catalog names or counts are fixed here.
        var rows = columns.Length == 0 ? Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Graph.Nodes.Count))) : columns.Max(column => column.Count());
        foreach (var column in Graph.Nodes.Where(node => !linked.Contains(node.Id)).Chunk(rows))
        {
            var y = (float)top;
            foreach (var node in column)
            {
                _bounds.Add(node.Id, new RectangleF(new PointF(left, y), sizes[node.Id]));
                y += sizes[node.Id].Height + 14;
            }
            left += column.Max(node => sizes[node.Id].Width) + 22;
        }
        _contentSize = _bounds.Count == 0 ? SizeF.Empty : new SizeF(
            _bounds.Values.Max(rect => rect.Right) + 56, _bounds.Values.Max(rect => rect.Bottom) + 20);
    }

    internal void SelectPackage(string id)
    {
        if (!_bounds.ContainsKey(id)) return;
        SelectedId = id;
        AccessibleDescription = Describe(Graph.Nodes.First(node => string.Equals(node.Id, id, StringComparison.OrdinalIgnoreCase)));
        Invalidate();
        PackageSelected?.Invoke(id);
    }

    internal void Fit()
    {
        _fitOnResize = true;
        var readableZoom = _bounds.Count == 0 ? .12f : Math.Min(1, 18 / _bounds.Values.Min(rect => rect.Height));
        Zoom = _contentSize.IsEmpty ? 1 : Math.Clamp(Math.Min(
            Math.Max(1, ClientSize.Width - 20) / _contentSize.Width,
            Math.Max(1, ClientSize.Height - 20) / _contentSize.Height), readableZoom, 1.3f);
        _offset = new PointF((ClientSize.Width - _contentSize.Width * Zoom) / 2,
            (ClientSize.Height - _contentSize.Height * Zoom) / 2);
        if (SelectedId is not null && _bounds.TryGetValue(SelectedId, out var selected) &&
            (_contentSize.Width * Zoom > ClientSize.Width || _contentSize.Height * Zoom > ClientSize.Height))
            _offset = new PointF(ClientSize.Width / 2f - (selected.Left + selected.Width / 2) * Zoom,
                ClientSize.Height / 2f - (selected.Top + selected.Height / 2) * Zoom);
        Invalidate();
    }

    internal void ZoomBy(float factor) => ZoomAt(factor, new Point(ClientSize.Width / 2, ClientSize.Height / 2));

    private void ZoomAt(float factor, Point anchor)
    {
        var next = Math.Clamp(Zoom * factor, .12f, 2.5f);
        _offset = new PointF(anchor.X - (anchor.X - _offset.X) * next / Zoom,
            anchor.Y - (anchor.Y - _offset.Y) * next / Zoom);
        Zoom = next;
        _fitOnResize = false;
        Invalidate();
    }

    private string? Hit(Point point)
    {
        var world = new PointF((point.X - _offset.X) / Zoom, (point.Y - _offset.Y) / Zoom);
        foreach (var (id, bounds) in _bounds) if (bounds.Contains(world)) return id;
        return null;
    }

    private string Describe(LauncherPackageNode node)
    {
        var lines = new List<string> { node.Title, node.Id };
        if (node.Package is not null) lines.Add(_language.Text("Version {0}", node.Package.Version));
        if (node.Package is null) lines.Add(_language.Text("Missing package"));
        if (node.InCycle) lines.Add(_language.Text("Package cycle"));
        foreach (var edge in Graph.Edges.Where(edge => edge.Source == node.Id))
            foreach (var declaration in edge.Declarations)
                lines.Add($"→ {edge.Target}  [{_language.Text(declaration.Source)} → {_language.Text(declaration.Target)}]" +
                    (declaration.Optional ? " · " + _language.Text("Optional") : string.Empty) +
                    (edge.Unavailable ? " · " + _language.Text("Unavailable dependency") : string.Empty));
        return string.Join(Environment.NewLine, lines);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Graph.Nodes.Count == 0)
        {
            TextRenderer.DrawText(e.Graphics, _language.Text(_emptyMessage),
                Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var saved = g.Save();
        g.TranslateTransform(_offset.X, _offset.Y);
        g.ScaleTransform(Zoom, Zoom);
        var focus = _hovered ?? SelectedId;
        var light = _palette.Panel.GetBrightness() > .5f;
        var dependencyColor = light ? DrawingColor.FromArgb(23, 95, 175) : DrawingColor.FromArgb(109, 187, 255);
        var dependentColor = light ? DrawingColor.FromArgb(151, 79, 17) : DrawingColor.FromArgb(245, 185, 106);
        bool IsRelated(LauncherPackageEdge edge) => edge.Source == focus || edge.Target == focus;
        var outgoing = Graph.Edges.ToLookup(edge => edge.Source);
        var incoming = Graph.Edges.ToLookup(edge => edge.Target);
        var dependencies = outgoing[focus ?? string.Empty].Select(edge => edge.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dependents = incoming[focus ?? string.Empty].Select(edge => edge.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool IsRelatedNode(string id) => id == focus || dependencies.Contains(id) || dependents.Contains(id);
        using var arrow = new AdjustableArrowCap(2.6f, 3.4f, true);
        // Keep routing stable while drawing focused links last, over crossings and unrelated links.
        foreach (var (edge, route) in Graph.Edges.Select((edge, index) => (edge, index)).OrderBy(item => IsRelated(item.edge)))
        {
            var from = _bounds[edge.Source];
            var to = _bounds[edge.Target];
            var selected = IsRelated(edge);
            var width = (selected ? 2.6f : 1) / Zoom;
            var color = edge.Source == focus ? dependencyColor : dependentColor;
            using var pen = new Pen(selected ? color : DrawingColor.FromArgb(focus is null ? 165 : 65, _palette.Secondary), width)
            {
                CustomEndCap = arrow,
                DashStyle = edge.Declarations.All(item => item.Optional) ? DashStyle.Dash : DashStyle.Solid,
                LineJoin = LineJoin.Round
            };
            var sourceEdges = outgoing[edge.Source].ToArray();
            var targetEdges = incoming[edge.Target].ToArray();
            var sourceIndex = Array.IndexOf(sourceEdges, edge);
            var targetIndex = Array.IndexOf(targetEdges, edge);
            var start = new PointF(from.Right, from.Top + from.Height * (sourceIndex + 1) / (sourceEdges.Length + 1));
            var end = new PointF(to.Left, to.Top + to.Height * (targetIndex + 1) / (targetEdges.Length + 1));
            using var path = new GraphicsPath();
            if (_columns[edge.Source] == _columns[edge.Target])
            {
                end.X = to.Right;
                var exit = new PointF(_columnRights[_columns[edge.Source]] + 8, start.Y);
                var entry = new PointF(exit.X, end.Y);
                var turn = _columnRights[_columns[edge.Source]] + 48;
                path.AddLine(start, exit);
                path.AddBezier(exit, new PointF(turn, start.Y), new PointF(turn, end.Y), entry);
                path.AddLine(entry, end);
            }
            else if (_edgeLanes.TryGetValue(edge, out var lane))
            {
                // Skip-layer links travel above the nodes, so direct dependencies remain visible.
                var exitX = _columnRights[_columns[edge.Source]] + 12 + route % 5 * 7;
                var entryX = end.X - 12 - targetIndex % 5 * 7;
                path.AddLines([start, new PointF(exitX, start.Y),
                    new PointF(exitX, lane), new PointF(entryX, lane),
                    new PointF(entryX, end.Y), end]);
            }
            else
            {
                var exit = new PointF(_columnRights[_columns[edge.Source]] + 8, start.Y);
                var gap = (end.X - exit.X) / 2;
                path.AddLine(start, exit);
                path.AddBezier(exit, new PointF(exit.X + gap, start.Y), new PointF(end.X - gap, end.Y), end);
            }
            using var halo = new Pen(_palette.Panel, width + (selected ? 3 : 1) / Zoom) { LineJoin = LineJoin.Round };
            g.DrawPath(halo, path);
            g.DrawPath(pen, path);
            if (edge.Unavailable)
            {
                using var warning = new SolidBrush(_palette.Warning);
                g.FillEllipse(warning, end.X - 13 / Zoom, end.Y - 3 / Zoom, 6 / Zoom, 6 / Zoom);
            }
        }
        foreach (var node in Graph.Nodes)
        {
            var rect = _bounds[node.Id];
            var focused = node.Id == focus;
            var related = IsRelatedNode(node.Id);
            var warning = node.Package is null || node.InCycle;
            var color = focused ? _palette.Primary : dependencies.Contains(node.Id) ? dependencyColor : dependentColor;
            using var fill = new SolidBrush(focused ? _palette.Field : _palette.Raised);
            using var outline = new Pen(focused ? _palette.Primary : related ? color : _palette.Border,
                (focused ? 2.8f : related ? 1.8f : 1) / Zoom);
            g.FillRectangle(fill, rect);
            if (related)
            {
                using var tint = new SolidBrush(DrawingColor.FromArgb(focused ? 30 : 18, color));
                g.FillRectangle(tint, rect);
            }
            g.DrawRectangle(outline, rect.X, rect.Y, rect.Width, rect.Height);
            if (!focused && dependencies.Contains(node.Id) && dependents.Contains(node.Id))
            {
                using var reverse = new Pen(dependentColor, 1.8f / Zoom);
                g.DrawLine(reverse, rect.Right, rect.Top, rect.Right, rect.Bottom);
            }
            if (warning)
            {
                using var marker = new SolidBrush(_palette.Warning);
                g.FillEllipse(marker, rect.Right - 9 / Zoom, rect.Top + 3 / Zoom, 6 / Zoom, 6 / Zoom);
            }
        }
        // Measure and draw titles with the same font/transform; names are never ellipsized or hidden.
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        foreach (var node in Graph.Nodes)
        {
            var rect = _bounds[node.Id];
            using var text = new SolidBrush(focus is null || IsRelatedNode(node.Id) ? _palette.Primary : _palette.Secondary);
            g.DrawString(node.Title, _titleFont, text, new PointF(rect.X + 12, rect.Y + 8), _titleFormat);
        }
        g.Restore(saved);
        if (Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -2, -2), ForeColor, BackColor);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); if (_fitOnResize) Fit(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); LayoutNodes(); Fit(); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); ZoomAt(MathF.Pow(1.15f, e.Delta / 120f), e.Location); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button is not (MouseButtons.Left or MouseButtons.Middle)) return;
        Focus();
        _mouseDown = e.Location;
        _panStart = _offset;
        _dragging = false;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Capture)
        {
            var dx = e.X - _mouseDown.X;
            var dy = e.Y - _mouseDown.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 4) _dragging = true;
            if (_dragging)
            {
                _offset = new PointF(_panStart.X + dx, _panStart.Y + dy);
                _fitOnResize = false;
                Cursor = Cursors.SizeAll;
                Invalidate();
            }
            return;
        }
        var id = Hit(e.Location);
        Cursor = id is null ? Cursors.Default : Cursors.Hand;
        if (_hovered == id) return;
        _hovered = id;
        _tooltip.SetToolTip(this, id is null ? null : Describe(Graph.Nodes.First(node => node.Id == id)));
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = null;
        _tooltip.SetToolTip(this, null);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!Capture) return;
        Capture = false;
        Cursor = Cursors.Default;
        if (!_dragging && e.Button == MouseButtons.Left && Hit(e.Location) is { } id) SelectPackage(id);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && Hit(e.Location) is { } id) PackageActivated?.Invoke(id);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
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
            default: return;
        }
        e.Handled = e.SuppressKeyPress = true;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tooltip.Dispose(); _titleFont.Dispose(); _titleFormat.Dispose(); }
        base.Dispose(disposing);
    }
}
