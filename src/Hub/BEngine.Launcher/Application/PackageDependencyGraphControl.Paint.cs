using System.Drawing.Drawing2D;
using DrawingColor = System.Drawing.Color;

namespace BEngine.Launcher;

internal sealed partial class PackageDependencyGraphControl
{
    private readonly AdjustableArrowCap _arrowCap = new(2.6f, 3.4f, true);
    private readonly Pen _routePen = new(DrawingColor.Gray) { LineJoin = LineJoin.Round, StartCap = LineCap.Round };
    private readonly Pen _haloPen = new(DrawingColor.Gray) { LineJoin = LineJoin.Round };
    private readonly Pen _hitPen = new(DrawingColor.Black), _outlinePen = new(DrawingColor.Gray);
    private readonly SolidBrush _fillBrush = new(DrawingColor.Gray), _textBrush = new(DrawingColor.White);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Graph.Nodes.Count == 0 || _layout is null)
        {
            TextRenderer.DrawText(e.Graphics, _language.Text(_emptyMessage), Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        var g = e.Graphics; var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias; g.SetClip(ClientRectangle);
        g.TranslateTransform(_offset.X, _offset.Y); g.ScaleTransform(Zoom, Zoom);
        var visible = new RectangleF(World(Point.Empty), new SizeF(ClientSize.Width / Zoom, ClientSize.Height / Zoom));
        var focus = SelectedId ?? _hovered;
        var light = _palette.Panel.GetBrightness() > .5f;
        var dependencyColor = light ? DrawingColor.FromArgb(23, 95, 175) : DrawingColor.FromArgb(109, 187, 255);
        var dependentColor = light ? DrawingColor.FromArgb(151, 79, 17) : DrawingColor.FromArgb(245, 185, 106);
        void DrawRoute(PackageGraphEdgeRoute route)
        {
            if (!RectangleF.Inflate(route.Bounds, 8 / Zoom, 8 / Zoom).IntersectsWith(visible)) return;
            var edge = Graph.Edges[route.Edge]; var kind = _edgeKinds[route.Edge];
            _routePen.Color = kind == 1 ? dependencyColor : kind == 2 ? dependentColor :
                DrawingColor.FromArgb(focus is null ? 140 : 55, _palette.Secondary);
            _routePen.Width = (kind == 0 ? 1 : 2.6f) / Zoom;
            _routePen.DashStyle = route.Optional ? DashStyle.Dash : DashStyle.Solid;
            _haloPen.Color = _palette.Panel; _haloPen.Width = _routePen.Width + (kind == 0 ? 1 : 3) / Zoom;
            g.DrawPath(_haloPen, route.Path); g.DrawPath(_routePen, route.Path);
            if (edge.Unavailable)
            {
                _fillBrush.Color = _palette.Warning;
                g.FillEllipse(_fillBrush, route.Target.X - 12 / Zoom, route.Target.Y - 2.5f / Zoom, 5 / Zoom, 5 / Zoom);
            }
        }
        foreach (var route in _layout.Routes) if (_edgeKinds[route.Edge] == 0) DrawRoute(route);
        foreach (var route in _layout.Routes) if (_edgeKinds[route.Edge] != 0) DrawRoute(route);
        var scale = DeviceDpi / 96f; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        foreach (var node in Graph.Nodes)
        {
            var rect = _bounds[node.Id]; if (!rect.IntersectsWith(visible)) continue;
            var focused = node.Id == focus;
            var dependency = _dependencies.Contains(node.Id); var dependent = _dependents.Contains(node.Id);
            var required = node.Package?.IsRequired == true;
            _fillBrush.Color = focused ? _palette.Field : _palette.Raised; g.FillRectangle(_fillBrush, rect);
            var color = focused ? _palette.Primary : dependency ? dependencyColor : dependentColor;
            if (!required && (focused || dependency || dependent))
            { _fillBrush.Color = DrawingColor.FromArgb(focused ? 30 : 18, color); g.FillRectangle(_fillBrush, rect); }
            // Required state stays neutral, independently of relationship highlighting.
            _outlinePen.Color = required || focused ? _palette.Primary : dependency || dependent ? color : _palette.Border;
            _outlinePen.Width = (required ? focused ? 3.2f : 2.6f : focused ? 2.4f : dependency || dependent ? 1.6f : 1) / Zoom;
            g.DrawRectangle(_outlinePen, rect.X, rect.Y, rect.Width, rect.Height);
            if (!required && !focused && dependency && dependent)
            { _outlinePen.Color = dependentColor; g.DrawLine(_outlinePen, rect.Right, rect.Top, rect.Right, rect.Bottom); }
            if (node.Package is null || node.InCycle)
            { _fillBrush.Color = _palette.Warning; g.FillEllipse(_fillBrush, rect.Right - 12 * scale, rect.Top + 14 * scale, 5 * scale, 5 * scale); }
            _textBrush.Color = focus is null || focused || dependency || dependent || required ? _palette.Primary : _palette.Secondary;
            g.DrawString(node.Title, _titleFont, _textBrush, new PointF(rect.X + 12 * scale, rect.Y + 8 * scale), _titleFormat);
            if (required) DrawRequiredBadge(g, rect, scale);
        }
        g.Restore(saved);
        if (Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -2, -2), ForeColor, BackColor);
    }
    private void DrawRequiredBadge(Graphics graphics, RectangleF bounds, float scale)
    {
        var x = bounds.Left + 10 * scale; var y = bounds.Bottom - _requiredBadgeSize.Height - 8 * scale;
        _outlinePen.Color = _palette.Primary; _outlinePen.Width = 1 / Zoom;
        graphics.DrawRectangle(_outlinePen, x, y, _requiredBadgeSize.Width, _requiredBadgeSize.Height);
        PackageBrowserDrawing.Lock(graphics, _outlinePen, x + 6 * scale, y + (_requiredBadgeSize.Height - 13 * scale) / 2, scale);
        _textBrush.Color = _palette.Primary;
        graphics.DrawString(_language.Text("Required"), _badgeFont, _textBrush, new PointF(x + 23 * scale, y + 3 * scale), _titleFormat);
    }
}
