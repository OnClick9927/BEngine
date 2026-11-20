using System.Drawing.Drawing2D;
using DrawingColor = System.Drawing.Color;

namespace BEngine.Launcher;

internal sealed record PackageDiagramNode(string PackageId, LauncherPackageEdge? Edge, bool Incoming);
internal sealed record PackageDiagramLink(PackageDiagramNode Node, GraphicsPath Path, PointF Source, PointF Target);

// One-hop graph: the root stays visible while either neighbor column scrolls independently.
internal sealed class PackageDependencyDiagramControl : Control
{
    private readonly VScrollBar _dependencyScroll = new(), _dependentScroll = new();
    private readonly ToolTip _tooltip = new() { AutoPopDelay = 20000 };
    private readonly System.Drawing.Font _nodeFont = new("Segoe UI", 10.5f);
    private readonly System.Drawing.Font _rootFont = new("Segoe UI", 11, FontStyle.Bold);
    private readonly System.Drawing.Font _headingFont = new("Segoe UI", 10, FontStyle.Bold);
    private readonly Pen _pen = new(DrawingColor.Gray), _hitPen = new(DrawingColor.Gray);
    private readonly SolidBrush _brush = new(DrawingColor.Gray);
    private readonly StringFormat _textFormat = new() { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
    private LauncherPalette _palette = LauncherPalette.From(new());
    private HubLocalization _language = new("system");
    private PackageDiagramNode[] _dependencies = [], _dependents = [];
    private Rectangle[] _dependencyBoxes = [], _dependentBoxes = [];
    private readonly List<PackageDiagramLink> _links = [];
    private Rectangle _body, _rootBox, _leftColumn, _rightColumn, _leftHeading, _rightHeading;
    private int _rowHeight, _nodeHeight;
    private bool _layingOut, _doubleClick, _stackedRoot;
    private string? _hoverKey;
    internal PackageDependencyIndex? Index { get; private set; }
    internal string? RootId { get; private set; }
    internal string? SelectedId { get; private set; }
    internal IReadOnlyList<PackageDiagramNode> Dependencies => _dependencies;
    internal IReadOnlyList<PackageDiagramNode> Dependents => _dependents;
    internal IReadOnlyList<Rectangle> DependencyBoxes => _dependencyBoxes;
    internal IReadOnlyList<Rectangle> DependentBoxes => _dependentBoxes;
    internal IReadOnlyList<PackageDiagramLink> Links => _links;
    internal Rectangle RootBox => _rootBox;
    internal int Revision { get; private set; }
    internal int GeometryRevision { get; private set; }
    internal event Action<string?>? PackageSelected;
    internal event Action<string>? PackageActivated;

    internal PackageDependencyDiagramControl()
    {
        DoubleBuffered = ResizeRedraw = TabStop = true;
        AccessibleRole = AccessibleRole.Diagram;
        Controls.AddRange([_dependencyScroll, _dependentScroll]);
        _dependencyScroll.ValueChanged += (_, _) => ScrollChanged();
        _dependentScroll.ValueChanged += (_, _) => ScrollChanged();
    }

    internal void SetAppearance(LauncherPalette palette, HubLocalization language)
    {
        _palette = palette; _language = language;
        BackColor = palette.Panel; ForeColor = palette.Primary;
        AccessibleName = language.Text("Package dependencies");
        _dependencyScroll.AccessibleName = language.Text("Dependencies");
        _dependentScroll.AccessibleName = language.Text("Used by");
        ClearHover(); Invalidate();
    }

    internal void SetRoot(PackageDependencyIndex index, string root)
    {
        root = index.Nodes[root].Id;
        if (ReferenceEquals(Index, index) && RootId == root) return;
        Index = index; RootId = SelectedId = root;
        _dependencies = Gather3650(index.Adjacent(root, false));
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Launcher.PackageDiagramNode[] Gather3650(global::System.Collections.Generic.IEnumerable<global::BEngine.Launcher.LauncherPackageEdge> items3650)
        {
            ArgumentNullException.ThrowIfNull(items3650, "source");
            global::BEngine.Launcher.PackageDiagramNode Evaluate3650(global::BEngine.Launcher.LauncherPackageEdge edge) => new PackageDiagramNode(edge.Target, edge, false);
            global::System.ReadOnlySpan<global::BEngine.Launcher.LauncherPackageEdge> values;
            if (items3650.GetType() == typeof(global::BEngine.Launcher.LauncherPackageEdge[]))
                values = (global::BEngine.Launcher.LauncherPackageEdge[])items3650;
            else if (items3650.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageEdge>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageEdge>)items3650);
            else
            {
                return global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(items3650, Evaluate3650));
            }

            if (values.IsEmpty)
                return global::System.Array.Empty<global::BEngine.Launcher.PackageDiagramNode>();
            var result = new global::BEngine.Launcher.PackageDiagramNode[values.Length];
            for (var index = 0; index < values.Length; index++)
                result[index] = Evaluate3650(values[index]);
            return result;
        }
        _dependents = Gather3776(index.Adjacent(root, true));
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Launcher.PackageDiagramNode[] Gather3776(global::System.Collections.Generic.IEnumerable<global::BEngine.Launcher.LauncherPackageEdge> items3776)
        {
            ArgumentNullException.ThrowIfNull(items3776, "source");
            global::BEngine.Launcher.PackageDiagramNode Evaluate3776(global::BEngine.Launcher.LauncherPackageEdge edge) => new PackageDiagramNode(edge.Source, edge, true);
            global::System.ReadOnlySpan<global::BEngine.Launcher.LauncherPackageEdge> values;
            if (items3776.GetType() == typeof(global::BEngine.Launcher.LauncherPackageEdge[]))
                values = (global::BEngine.Launcher.LauncherPackageEdge[])items3776;
            else if (items3776.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageEdge>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageEdge>)items3776);
            else
            {
                return global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(items3776, Evaluate3776));
            }

            if (values.IsEmpty)
                return global::System.Array.Empty<global::BEngine.Launcher.PackageDiagramNode>();
            var result = new global::BEngine.Launcher.PackageDiagramNode[values.Length];
            for (var index = 0; index < values.Length; index++)
                result[index] = Evaluate3776(values[index]);
            return result;
        }
        _layingOut = true;
        try { _dependencyScroll.Value = _dependentScroll.Value = 0; }
        finally { _layingOut = false; }
        Revision++; Reflow();
    }

    internal void ClearData()
    {
        if (Index is null) return;
        Index = null; RootId = SelectedId = null;
        _dependencies = _dependents = []; _dependencyBoxes = _dependentBoxes = [];
        DisposeLinks(); _dependencyScroll.Visible = _dependentScroll.Visible = false;
        Revision++; ClearHover(); Invalidate();
    }

    internal void FocusRoot() => Focus();
    internal void SelectPackage(string? id)
    { SelectedId = id; ClearHover(); Invalidate(); PackageSelected?.Invoke(id); }

    private void Reflow()
    {
        if (_layingOut) return;
        _layingOut = true;
        try
        {
            var scale = DeviceDpi / 96f;
            var padding = (int)MathF.Ceiling(8 * scale);
            var heading = TextRenderer.MeasureText("Ag", _headingFont).Height + padding * 2;
            _nodeHeight = Math.Max(TextRenderer.MeasureText("Ag", _rootFont).Height, TextRenderer.MeasureText("Ag", _nodeFont).Height) + (int)MathF.Ceiling(14 * scale);
            _rowHeight = _nodeHeight + padding;
            var width = Math.Max(1, ClientSize.Width - padding * 2);
            var gap = Math.Min((int)MathF.Ceiling(32 * scale), width / 10);
            var rootWidth = Math.Min((int)MathF.Ceiling(150 * scale), width / 3);
            _stackedRoot = width < 700 * scale;
            if (_stackedRoot)
            {
                gap = Math.Min((int)MathF.Ceiling(40 * scale), width / 8);
                var sideWidth = Math.Max(1, (width - gap) / 2);
                _rootBox = new Rectangle(padding + (width - rootWidth) / 2, padding, rootWidth, _nodeHeight);
                var headingY = _rootBox.Bottom + padding;
                _body = new Rectangle(0, headingY + heading, ClientSize.Width, Math.Max(1, ClientSize.Height - headingY - heading - padding));
                _leftColumn = new Rectangle(padding, _body.Y, sideWidth, _body.Height);
                _rightColumn = new Rectangle(padding + sideWidth + gap, _body.Y, sideWidth, _body.Height);
                _leftHeading = new Rectangle(_leftColumn.X, headingY, sideWidth, heading);
                _rightHeading = new Rectangle(_rightColumn.X, headingY, sideWidth, heading);
            }
            else
            {
                _body = new Rectangle(0, heading, ClientSize.Width, Math.Max(1, ClientSize.Height - heading - padding));
                var sideWidth = Math.Max(1, (width - rootWidth - gap * 2) / 2);
                _leftColumn = new Rectangle(padding, _body.Y, sideWidth, _body.Height);
                _rootBox = new Rectangle(padding + sideWidth + gap, _body.Y + Math.Max(0, (_body.Height - _nodeHeight) / 2), rootWidth, _nodeHeight);
                _rightColumn = new Rectangle(_rootBox.Right + gap, _body.Y, sideWidth, _body.Height);
                _leftHeading = new Rectangle(_leftColumn.X, 0, sideWidth, heading);
                _rightHeading = new Rectangle(_rightColumn.X, 0, sideWidth, heading);
            }
            ConfigureScroll(_dependencyScroll, _leftColumn, _dependencies.Length, true);
            ConfigureScroll(_dependentScroll, _rightColumn, _dependents.Length, false);
            BuildGeometry();
        }
        finally { _layingOut = false; }
        ClearHover(); Invalidate();
    }

    private void ConfigureScroll(VScrollBar scroll, Rectangle column, int count, bool left)
    {
        var totalHeight = Math.Max(0, count * _rowHeight - (_rowHeight - _nodeHeight));
        scroll.SetBounds(left ? column.Left : column.Right - scroll.Width, column.Top, scroll.Width, column.Height);
        scroll.LargeChange = Math.Max(1, column.Height); scroll.SmallChange = Math.Max(1, _rowHeight);
        scroll.Maximum = Math.Max(0, totalHeight - 1);
        scroll.Value = Math.Min(scroll.Value, Math.Max(0, scroll.Maximum - scroll.LargeChange + 1));
        scroll.Visible = Index is not null && totalHeight > column.Height;
    }

    private void ScrollChanged()
    { if (_layingOut) return; BuildGeometry(); ClearHover(); Invalidate(); }

    private void BuildGeometry()
    {
        DisposeLinks();
        _dependencyBoxes = Boxes(_dependencies, _leftColumn, _dependencyScroll, true);
        _dependentBoxes = Boxes(_dependents, _rightColumn, _dependentScroll, false);
        GeometryRevision++;
    }

    private Rectangle[] Boxes(PackageDiagramNode[] nodes, Rectangle column, VScrollBar scroll, bool left)
    {
        var scale = DeviceDpi / 96f;
        var reserved = scroll.Visible ? scroll.Width + (int)MathF.Ceiling(4 * scale) : 0;
        var totalHeight = nodes.Length * _rowHeight - (_rowHeight - _nodeHeight);
        var start = column.Top + Math.Max(0, (column.Height - totalHeight) / 2) - scroll.Value;
        var boxes = new Rectangle[nodes.Length];
        for (var i = 0; i < nodes.Length; i++)
        {
            var box = boxes[i] = new Rectangle(column.Left + (left ? reserved : 0), start + i * _rowHeight, Math.Max(1, column.Width - reserved), _nodeHeight);
            if (box.Bottom <= _body.Top || box.Top >= _body.Bottom) continue;
            // All arrows point from the user (on the right) toward its dependency (on the left).
            var portOffset = (_rightColumn.Left - _leftColumn.Right) / 5f;
            var source = left ? _stackedRoot ? new PointF(_rootBox.Left + _rootBox.Width / 2f - portOffset, _rootBox.Bottom + 2 * scale)
                    : new PointF(_rootBox.Left - 2 * scale, _rootBox.Top + _rootBox.Height / 2f)
                : new PointF(box.Left - 2 * scale, box.Top + box.Height / 2f);
            var target = left ? new PointF(box.Right + 2 * scale, box.Top + box.Height / 2f)
                : _stackedRoot ? new PointF(_rootBox.Left + _rootBox.Width / 2f + portOffset, _rootBox.Bottom + 2 * scale)
                    : new PointF(_rootBox.Right + 2 * scale, _rootBox.Top + _rootBox.Height / 2f);
            var middle = (source.X + target.X) / 2;
            var path = new GraphicsPath();
            if (_stackedRoot)
                path.AddBezier(source, new PointF(left ? source.X : middle, left ? target.Y : source.Y),
                    new PointF(left ? middle : target.X, left ? target.Y : source.Y), target);
            else path.AddBezier(source, new PointF(middle, source.Y), new PointF(middle, target.Y), target);
            _links.Add(new PackageDiagramLink(nodes[i], path, source, target));
        }
        return boxes;
    }

    private void DisposeLinks() { foreach (var link in _links) link.Path.Dispose(); _links.Clear(); }
    private void ClearHover() { _hoverKey = null; _tooltip.SetToolTip(this, null); Cursor = Cursors.Default; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Index is null || RootId is null) return;
        var graphics = e.Graphics; var state = graphics.Save(); var scale = DeviceDpi / 96f;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        _brush.Color = _palette.Secondary;
        graphics.DrawString(_language.Text("Dependencies") + $" ({_dependencies.Length})", _headingFont, _brush,
            _leftHeading, _textFormat);
        graphics.DrawString(_language.Text("Used by") + $" ({_dependents.Length})", _headingFont, _brush,
            _rightHeading, _textFormat);
        foreach (var link in _links)
        {
            var edge = link.Node.Edge!;
            var selected = SelectedId == RootId || SelectedId == link.Node.PackageId;
            _pen.Color = edge.Unavailable ? _palette.Warning : selected ? _palette.Secondary : DrawingColor.FromArgb(120, _palette.Secondary);
            _pen.Width = (selected ? 1.5f : 1) * scale;
            _pen.DashStyle = Scan11691(edge.Declarations) ? DashStyle.Dash : DashStyle.Solid;
            [global::BEngine.SkipLoopGuard]
            bool Scan11691(global::System.Collections.Generic.IEnumerable<global::BEngine.Launcher.LauncherPackageDependency> scanSource11691)
            {
                ArgumentNullException.ThrowIfNull(scanSource11691, "source");
                global::System.ReadOnlySpan<global::BEngine.Launcher.LauncherPackageDependency> scanValues11691;
                if (scanSource11691.GetType() == typeof(global::BEngine.Launcher.LauncherPackageDependency[]))
                    scanValues11691 = (global::BEngine.Launcher.LauncherPackageDependency[])scanSource11691;
                else if (scanSource11691.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageDependency>))
                    scanValues11691 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Launcher.LauncherPackageDependency>)scanSource11691);
                else
                {
                    foreach (var scanItem11691 in scanSource11691)
                    {
                        if (!Match11691(scanItem11691))
                            return false;
                    }
            
                    return true;
                }
            
                foreach (var scanItem11691 in scanValues11691)
                {
                    if (!Match11691(scanItem11691))
                        return false;
                }
            
                return true;
                bool Match11691(global::BEngine.Launcher.LauncherPackageDependency item) => item.Optional;
            }
            graphics.DrawPath(_pen, link.Path); _pen.DashStyle = DashStyle.Solid;
            if (_stackedRoot && link.Node.Incoming)
            {
                graphics.DrawLine(_pen, link.Target, new PointF(link.Target.X - 3 * scale, link.Target.Y + 5 * scale));
                graphics.DrawLine(_pen, link.Target, new PointF(link.Target.X + 3 * scale, link.Target.Y + 5 * scale));
            }
            else
            {
                graphics.DrawLine(_pen, link.Target, new PointF(link.Target.X + 5 * scale, link.Target.Y - 3 * scale));
                graphics.DrawLine(_pen, link.Target, new PointF(link.Target.X + 5 * scale, link.Target.Y + 3 * scale));
            }
        }
        graphics.SetClip(_body);
        DrawNodes(graphics, _dependencies, _dependencyBoxes);
        DrawNodes(graphics, _dependents, _dependentBoxes);
        if (_dependencies.Length == 0) DrawEmpty(graphics, _leftColumn);
        if (_dependents.Length == 0) DrawEmpty(graphics, _rightColumn);
        graphics.SetClip(ClientRectangle);
        DrawNode(graphics, RootId, _rootBox, null, true);
        graphics.Restore(state);
    }

    private void DrawEmpty(Graphics graphics, Rectangle column)
    {
        _brush.Color = _palette.Secondary; _textFormat.Alignment = StringAlignment.Center;
        graphics.DrawString(_language.Text("None"), _nodeFont, _brush, column, _textFormat);
        _textFormat.Alignment = StringAlignment.Near;
    }
    private void DrawNodes(Graphics graphics, PackageDiagramNode[] nodes, Rectangle[] boxes)
    {
        for (var i = 0; i < nodes.Length; i++)
            if (boxes[i].Bottom > _body.Top && boxes[i].Top < _body.Bottom) DrawNode(graphics, nodes[i].PackageId, boxes[i], nodes[i].Edge, false);
    }
    private void DrawNode(Graphics graphics, string id, Rectangle box, LauncherPackageEdge? edge, bool root)
    {
        var scale = DeviceDpi / 96f; var node = Index!.Nodes[id]; var required = node.Package?.IsRequired == true;
        var selected = SelectedId == id;
        _brush.Color = selected ? _palette.Field : _palette.Raised; graphics.FillRectangle(_brush, box);
        _pen.Color = required || selected || root ? _palette.Primary : _palette.Border;
        _pen.Width = (required ? 2.4f : root || selected ? 1.6f : 1) * scale; graphics.DrawRectangle(_pen, box);
        var inset = 7 * scale; var lockWidth = required ? 17 * scale : 0;
        var text = new RectangleF(box.Left + inset + lockWidth, box.Top, Math.Max(1, box.Width - inset * 2 - lockWidth), box.Height);
        _brush.Color = node.Package is null || node.InCycle || edge?.Unavailable == true ? _palette.Warning : _palette.Primary;
        var title = node.Package is null ? node.Title + " · " + _language.Text("Missing package") : node.Title;
        graphics.DrawString(title, root ? _rootFont : _nodeFont, _brush, text, _textFormat);
        if (required) { _pen.Color = _palette.Primary; PackageBrowserDrawing.Lock(graphics, _pen, box.Left + inset, box.Top + (box.Height - 13 * scale) / 2, scale); }
    }

    private PackageDiagramNode? HitNode(Point point)
    {
        if (Index is null) return null;
        if (_rootBox.Contains(point)) return new PackageDiagramNode(RootId!, null, false);
        if (!_body.Contains(point)) return null;
        for (var i = 0; i < _dependencies.Length; i++) if (_dependencyBoxes[i].Contains(point)) return _dependencies[i];
        for (var i = 0; i < _dependents.Length; i++) if (_dependentBoxes[i].Contains(point)) return _dependents[i];
        return null;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var node = HitNode(e.Location); PackageDiagramLink? link = null;
        if (node is null && ClientRectangle.Contains(e.Location))
        {
            _hitPen.Width = 7 * DeviceDpi / 96f;
            [global::BEngine.SkipLoopGuard]
            global::BEngine.Launcher.PackageDiagramLink? Query17270(global::System.Collections.Generic.IEnumerable<global::BEngine.Launcher.PackageDiagramLink> querySource17270)
            {
                ArgumentNullException.ThrowIfNull(querySource17270, "source");
                bool Matches17270(global::BEngine.Launcher.PackageDiagramLink item) => item.Path.IsOutlineVisible(e.Location, _hitPen);
                global::System.ReadOnlySpan<global::BEngine.Launcher.PackageDiagramLink> queryValues17270;
                if (querySource17270.GetType() == typeof(global::BEngine.Launcher.PackageDiagramLink[]))
                    queryValues17270 = (global::BEngine.Launcher.PackageDiagramLink[])querySource17270;
                else if (querySource17270.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Launcher.PackageDiagramLink>))
                    queryValues17270 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Launcher.PackageDiagramLink>)querySource17270);
                else
                {
                    foreach (var item in querySource17270)
                        if (Matches17270(item))
                            return item;
                    return default!;
                }
            
                foreach (var item in queryValues17270)
                    if (Matches17270(item))
                        return item;
                return default!;
            }
            
            link = Query17270(_links);
        }
        var key = node is not null ? $"node:{node.PackageId}:{node.Incoming}" : link is not null ? $"edge:{link.Node.PackageId}:{link.Node.Incoming}" : null;
        if (_hoverKey == key) return;
        _hoverKey = key; Cursor = node is not null ? Cursors.Hand : Cursors.Default;
        string? message = null;
        if (node is not null)
        {
            var entry = Index!.Nodes[node.PackageId];
            message = entry.Title + Environment.NewLine + entry.Id;
            if (entry.Package is null) message += Environment.NewLine + _language.Text("Missing package");
            if (entry.Package?.IsRequired == true) message += Environment.NewLine + _language.Text("Required package");
            if (entry.InCycle) message += Environment.NewLine + _language.Text("Assembly dependency cycle");
            if (node.Edge is not null) message += Environment.NewLine + Index.Describe(node.Edge, _language);
        }
        else if (link is not null) message = Index!.Describe(link.Node.Edge!, _language);
        _tooltip.SetToolTip(this, message);
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); ClearHover(); }
    protected override void OnMouseDown(MouseEventArgs e)
    { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); if (e.Clicks == 1) _doubleClick = false; } }
    protected override void OnMouseUp(MouseEventArgs e)
    { base.OnMouseUp(e); if (e.Button == MouseButtons.Left && !_doubleClick) SelectPackage(HitNode(e.Location)?.PackageId); }
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && HitNode(e.Location) is { } node)
        { _doubleClick = true; PackageActivated?.Invoke(node.PackageId); }
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var scroll = e.X < _rootBox.Left ? _dependencyScroll : e.X > _rootBox.Right ? _dependentScroll
            : _dependentScroll.Visible ? _dependentScroll : _dependencyScroll;
        scroll.Value = Math.Clamp(scroll.Value - e.Delta / 120 * Math.Max(1, _rowHeight) * 3, 0, Math.Max(0, scroll.Maximum - scroll.LargeChange + 1));
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) SelectPackage(null);
        else if (e.KeyCode == Keys.Enter && SelectedId is { } id) PackageActivated?.Invoke(id);
        else if (e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End)
        {
            bool __queryResult18511;
            {
                var __querySource18511 = _dependents;
                ArgumentNullException.ThrowIfNull(__querySource18511, "source");
                __queryResult18511 = false;
                foreach (var __queryItem18511 in __querySource18511)
                {
                    if ((__queryItem18511.PackageId == SelectedId))
                    {
                        __queryResult18511 = true;
                        break;
                    }
                }
            }
            
            var incoming = __queryResult18511;
            var nodes = incoming ? _dependents : _dependencies; var scroll = incoming ? _dependentScroll : _dependencyScroll;
            var position = Array.FindIndex(nodes, node => node.PackageId == SelectedId);
            if (e.KeyCode is Keys.Left or Keys.Right)
            {
                if ((e.KeyCode == Keys.Left && incoming) || (e.KeyCode == Keys.Right && !incoming && SelectedId != RootId)) SelectPackage(RootId);
                else
                {
                    incoming = e.KeyCode == Keys.Right; nodes = incoming ? _dependents : _dependencies;
                    if (nodes.Length != 0) SelectPackage(nodes[0].PackageId);
                }
            }
            else if (nodes.Length != 0)
            {
                position = e.KeyCode switch { Keys.Home => 0, Keys.End => nodes.Length - 1, Keys.Up => position - 1, _ => position + 1 };
                position = Math.Clamp(position, 0, nodes.Length - 1); SelectPackage(nodes[position].PackageId);
                var top = position * _rowHeight; var bottom = top + _nodeHeight;
                var value = top < scroll.Value ? top : bottom > scroll.Value + _body.Height ? bottom - _body.Height : scroll.Value;
                scroll.Value = Math.Clamp(value, 0, Math.Max(0, scroll.Maximum - scroll.LargeChange + 1));
            }
        }
        else return;
        e.Handled = e.SuppressKeyPress = true;
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Reflow(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); Reflow(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { DisposeLinks(); _tooltip.Dispose(); _nodeFont.Dispose(); _rootFont.Dispose(); _headingFont.Dispose(); _pen.Dispose(); _hitPen.Dispose(); _brush.Dispose(); _textFormat.Dispose(); }
        base.Dispose(disposing);
    }
}
