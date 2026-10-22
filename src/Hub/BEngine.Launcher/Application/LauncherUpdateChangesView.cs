using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace BEngine.Launcher;

/// <summary>A file tree with independent name, size and change columns.</summary>
internal sealed class LauncherUpdateChangesView : UserControl
{
    private readonly ChangesTree _tree = new();
    private readonly Label _empty = new()
    {
        Dock = DockStyle.Fill,
        Text = "No file changes",
        TextAlign = ContentAlignment.MiddleCenter,
        Padding = new Padding(12),
        AccessibleRole = AccessibleRole.StaticText
    };
    private readonly ChangesHeader _header;
    private readonly ImageList _images = new() { ColorDepth = ColorDepth.Depth32Bit };
    private readonly Dictionary<string, int> _iconIndices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Bitmap> _ownedIcons = [];
    private Func<string, string> _localize = static text => text;
    private LauncherPalette _palette = LauncherPalette.From(new LauncherPersonalization());
    private int _nameEnd;
    private int _sizeEnd;

    internal LauncherUpdateChangesView()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _header = new ChangesHeader(this) { Dock = DockStyle.Top };
        _tree.Dock = DockStyle.Fill;
        _tree.DrawMode = TreeViewDrawMode.OwnerDrawAll;
        _tree.FullRowSelect = true;
        _tree.HideSelection = false;
        _tree.ShowNodeToolTips = true;
        _tree.ShowLines = false;
        _tree.ShowRootLines = true;
        _tree.BorderStyle = BorderStyle.None;
        _tree.ImageList = _images;
        _tree.AccessibleName = "Update file changes";
        _tree.Controls.Add(_empty);
        _tree.DrawNode += DrawNode;
        _tree.AfterSelect += (_, _) => _tree.Invalidate();
        _tree.AfterExpand += (_, _) => _tree.Invalidate();
        _tree.AfterCollapse += (_, _) => _tree.Invalidate();
        _tree.ClientSizeChanged += (_, _) => UpdateColumns();
        _tree.Enter += (_, _) => _tree.Invalidate();
        _tree.Leave += (_, _) => _tree.Invalidate();
        Controls.Add(_tree);
        Controls.Add(_header);
        Padding = new Padding(1);
        AccessibleName = "Update file changes";
        UpdateMetrics();
        ApplyPalette(_palette);
    }

    internal void SetChanges(IReadOnlyList<LauncherUpdateChange> changes, Func<string, string> localize)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(localize);
        _localize = localize;
        _empty.Text = localize("No file changes");
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            var directories = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in changes.OrderBy(change => change.Path, StringComparer.OrdinalIgnoreCase))
            {
                var parts = change.Path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                var nodes = _tree.Nodes;
                var prefix = string.Empty;
                for (var index = 0; index < parts.Length - 1; index++)
                {
                    prefix = prefix.Length == 0 ? parts[index] : $"{prefix}/{parts[index]}";
                    if (!directories.TryGetValue(prefix, out var directory))
                    {
                        directory = CreateNode(parts[index], new ChangeRow(prefix, true));
                        nodes.Add(directory);
                        directories.Add(prefix, directory);
                    }
                    ((ChangeRow)directory.Tag!).Add(change);
                    nodes = directory.Nodes;
                }
                var row = new ChangeRow(change.Path, false);
                row.Add(change);
                nodes.Add(CreateNode(parts[^1], row));
            }
            foreach (TreeNode root in _tree.Nodes) root.Expand();
            RefreshNodeDescriptions(_tree.Nodes);
            if (_tree.Nodes.Count > 0) _tree.TopNode = _tree.Nodes[0];
            _empty.Visible = _tree.Nodes.Count == 0;
        }
        finally
        {
            _tree.EndUpdate();
        }
        _header.Invalidate();
    }

    internal void ClearChanges()
    {
        _tree.Nodes.Clear();
        _empty.Visible = true;
        _tree.Invalidate();
    }

    internal void ReLocalize(Func<string, string> localize)
    {
        ArgumentNullException.ThrowIfNull(localize);
        _localize = localize;
        _empty.Text = localize("No file changes");
        RefreshNodeDescriptions(_tree.Nodes);
        _header.Invalidate();
        _tree.Invalidate();
    }

    internal void ApplyPalette(LauncherPalette palette)
    {
        _palette = palette;
        BackColor = palette.Border;
        ForeColor = palette.Primary;
        _header.BackColor = palette.Raised;
        _header.ForeColor = palette.Primary;
        _tree.BackColor = palette.Field;
        _tree.ForeColor = palette.Primary;
        _empty.BackColor = palette.Field;
        _empty.ForeColor = palette.Secondary;
        _header.Invalidate();
        _tree.Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateColumns();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (_tree is not null) UpdateMetrics();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ResetIcons();
        UpdateMetrics();
        foreach (var node in EnumerateNodes(_tree.Nodes))
        {
            var row = (ChangeRow)node.Tag!;
            node.ImageIndex = GetIconIndex(row.Path, row.Directory);
            node.SelectedImageIndex = node.ImageIndex;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tree.ImageList = null;
            _images.Dispose();
            foreach (var icon in _ownedIcons) icon.Dispose();
            _ownedIcons.Clear();
        }
        base.Dispose(disposing);
    }

    private TreeNode CreateNode(string name, ChangeRow row)
    {
        var imageIndex = GetIconIndex(row.Path, row.Directory);
        return new TreeNode(name) { Tag = row, ImageIndex = imageIndex, SelectedImageIndex = imageIndex };
    }

    private void UpdateMetrics()
    {
        var iconSize = LogicalToDeviceUnits(16);
        _images.ImageSize = new Size(iconSize, iconSize);
        _tree.ItemHeight = Math.Max(LogicalToDeviceUnits(26), Font.Height + LogicalToDeviceUnits(8));
        _tree.Indent = LogicalToDeviceUnits(18);
        _header.Height = Math.Max(LogicalToDeviceUnits(30), Font.Height + LogicalToDeviceUnits(10));
        UpdateColumns();
    }

    private void UpdateColumns()
    {
        if (_tree is null) return;
        // Preserve room for the name even in a narrow split pane. Every cell is clipped
        // independently, so a deep path or long filename cannot obscure other columns.
        var width = Math.Max(0, _tree.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
        var sizeWidth = Math.Min(LogicalToDeviceUnits(82), width * 3 / 10);
        var statusWidth = Math.Min(LogicalToDeviceUnits(112), width / 3);
        _nameEnd = Math.Max(0, width - sizeWidth - statusWidth);
        _sizeEnd = _nameEnd + sizeWidth;
        _tree.Invalidate();
        _header?.Invalidate();
    }

    private void DrawNode(object? sender, DrawTreeNodeEventArgs e)
    {
        if (e.Node?.Tag is not ChangeRow row || e.Bounds.Height <= 0) return;
        var graphics = e.Graphics;
        var rowBounds = new Rectangle(0, e.Bounds.Y, _tree.ClientSize.Width, e.Bounds.Height);
        var selected = e.Node == _tree.SelectedNode;
        var background = selected ? _palette.Accent : _palette.Field;
        var foreground = selected ? Color.White : _palette.Primary;
        using (var brush = new SolidBrush(background)) graphics.FillRectangle(brush, rowBounds);
        var previousClip = graphics.Save();
        graphics.SetClip(new Rectangle(0, e.Bounds.Y, Math.Max(0, _nameEnd), e.Bounds.Height));
        var iconSize = _images.ImageSize.Width;
        var iconX = e.Node.Bounds.Left - iconSize - LogicalToDeviceUnits(3);
        if (e.Node.Nodes.Count > 0)
        {
            var toggleSize = LogicalToDeviceUnits(9);
            var toggle = new Rectangle(iconX - _tree.Indent + (iconSize - toggleSize) / 2,
                e.Bounds.Y + (e.Bounds.Height - toggleSize) / 2, toggleSize, toggleSize);
            using var pen = new Pen(selected ? Color.White : _palette.Secondary);
            var center = toggle.Left + toggle.Width / 2;
            var centerY = toggle.Top + toggle.Height / 2;
            graphics.DrawRectangle(pen, toggle);
            graphics.DrawLine(pen, toggle.Left + 2, centerY, toggle.Right - 2, centerY);
            if (!e.Node.IsExpanded) graphics.DrawLine(pen, center, toggle.Top + 2, center, toggle.Bottom - 2);
        }
        _images.Draw(graphics, iconX, e.Bounds.Y + (e.Bounds.Height - iconSize) / 2, e.Node.ImageIndex);
        DrawText(graphics, e.Node.Text,
            new Rectangle(iconX + iconSize + LogicalToDeviceUnits(3), e.Bounds.Y,
                Math.Max(0, _nameEnd - iconX - iconSize - LogicalToDeviceUnits(7)), e.Bounds.Height), foreground);
        graphics.Restore(previousClip);

        var cellPadding = LogicalToDeviceUnits(6);
        DrawText(graphics, FormatBytes(row.Bytes), new Rectangle(_nameEnd + cellPadding, e.Bounds.Y,
            Math.Max(0, _sizeEnd - _nameEnd - cellPadding * 2), e.Bounds.Height), foreground, true);
        var statusLeft = _sizeEnd + cellPadding;
        var glyphSize = Math.Min(LogicalToDeviceUnits(16), e.Bounds.Height - 4);
        var glyph = new Rectangle(statusLeft, e.Bounds.Y + (e.Bounds.Height - glyphSize) / 2, glyphSize, glyphSize);
        if (row.SingleKind is { } kind)
            LauncherUpdateStatusGlyph.Draw(graphics, glyph, kind, foreground, background);
        else
            LauncherUpdateStatusGlyph.DrawMixed(graphics, glyph, foreground, background);
        DrawText(graphics, StatusText(row), new Rectangle(glyph.Right + LogicalToDeviceUnits(5), e.Bounds.Y,
            Math.Max(0, _tree.ClientSize.Width - glyph.Right - LogicalToDeviceUnits(12) - SystemInformation.VerticalScrollBarWidth), e.Bounds.Height), foreground);
        using var divider = new Pen(selected ? Color.FromArgb(70, Color.White) : _palette.Border);
        graphics.DrawLine(divider, _nameEnd, e.Bounds.Top, _nameEnd, e.Bounds.Bottom);
        graphics.DrawLine(divider, _sizeEnd, e.Bounds.Top, _sizeEnd, e.Bounds.Bottom);
        if (selected && _tree.Focused)
            ControlPaint.DrawFocusRectangle(graphics, new Rectangle(1, e.Bounds.Y + 1, _tree.ClientSize.Width - 2, e.Bounds.Height - 2), foreground, background);
    }

    private void DrawText(Graphics graphics, string value, Rectangle bounds, Color foreground, bool alignRight = false)
    {
        if (bounds.Width <= 0) return;
        TextRenderer.DrawText(graphics, value, Font, bounds, foreground,
            TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping |
            (alignRight ? TextFormatFlags.Right : TextFormatFlags.Left));
    }

    private string StatusText(ChangeRow row) => _localize(row.SingleKind switch
    {
        LauncherUpdateChangeKind.Added => "Added",
        LauncherUpdateChangeKind.Deleted => "Deleted",
        LauncherUpdateChangeKind.Modified => "Modified",
        _ => "Mixed"
    });

    private void RefreshNodeDescriptions(TreeNodeCollection nodes)
    {
        foreach (var node in EnumerateNodes(nodes))
        {
            var row = (ChangeRow)node.Tag!;
            node.ToolTipText = $"{row.Path}\n{FormatBytes(row.Bytes)} · {StatusText(row)}";
            if (row.Directory)
                node.ToolTipText += $"\n{_localize("Added")} {row.Added} · {_localize("Modified")} {row.Modified} · {_localize("Deleted")} {row.Deleted}";
        }
    }

    private static IEnumerable<TreeNode> EnumerateNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            foreach (var child in EnumerateNodes(node.Nodes)) yield return child;
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.0} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.0} MB",
        >= 1024 => $"{bytes / 1024d:0.0} KB",
        _ => $"{bytes} B"
    };

    private int GetIconIndex(string path, bool directory)
    {
        var extension = directory ? string.Empty : Path.GetExtension(path);
        var key = directory ? "<directory>" : extension;
        if (_iconIndices.TryGetValue(key, out var index)) return index;
        var icon = ShellFileIcons.Load(directory, extension, _images.ImageSize.Width);
        index = _images.Images.Count;
        _images.Images.Add(icon);
        _ownedIcons.Add(icon);
        _iconIndices.Add(key, index);
        return index;
    }

    private void ResetIcons()
    {
        _images.Images.Clear();
        _iconIndices.Clear();
        foreach (var icon in _ownedIcons) icon.Dispose();
        _ownedIcons.Clear();
    }

    private sealed class ChangeRow(string path, bool directory)
    {
        internal string Path { get; } = path;
        internal bool Directory { get; } = directory;
        internal long Bytes { get; private set; }
        internal int Added { get; private set; }
        internal int Modified { get; private set; }
        internal int Deleted { get; private set; }
        internal LauncherUpdateChangeKind? SingleKind => (Added > 0 ? 1 : 0) + (Modified > 0 ? 1 : 0) + (Deleted > 0 ? 1 : 0) != 1
            ? null : Added > 0 ? LauncherUpdateChangeKind.Added : Deleted > 0 ? LauncherUpdateChangeKind.Deleted : LauncherUpdateChangeKind.Modified;

        internal void Add(LauncherUpdateChange change)
        {
            Bytes += Math.Max(0, change.Size);
            if (change.Kind == LauncherUpdateChangeKind.Added) Added++;
            else if (change.Kind == LauncherUpdateChangeKind.Deleted) Deleted++;
            else Modified++;
        }
    }

    private sealed class ChangesTree : TreeView
    {
        internal ChangesTree() => SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                // Name cells use ellipsis; native horizontal scrolling would move the
                // hierarchy without moving the fixed size and status columns.
                parameters.Style |= 0x8000; // TVS_NOHSCROLL
                return parameters;
            }
        }
    }

    private sealed class ChangesHeader(LauncherUpdateChangesView owner) : Control
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var padding = owner.LogicalToDeviceUnits(6);
            owner.DrawText(e.Graphics, owner._localize("File"), new Rectangle(padding, 0, Math.Max(0, owner._nameEnd - padding * 2), Height), ForeColor);
            owner.DrawText(e.Graphics, owner._localize("Size"), new Rectangle(owner._nameEnd + padding, 0, Math.Max(0, owner._sizeEnd - owner._nameEnd - padding * 2), Height), ForeColor, true);
            owner.DrawText(e.Graphics, owner._localize("Status"), new Rectangle(owner._sizeEnd + padding, 0, Math.Max(0, Width - owner._sizeEnd - padding * 2), Height), ForeColor);
            using var pen = new Pen(owner._palette.Border);
            e.Graphics.DrawLine(pen, owner._nameEnd, 0, owner._nameEnd, Height);
            e.Graphics.DrawLine(pen, owner._sizeEnd, 0, owner._sizeEnd, Height);
            e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }

    private static class ShellFileIcons
    {
        internal static Bitmap Load(bool directory, string extension, int size)
        {
            const uint icon = 0x100, smallIcon = 0x1, useFileAttributes = 0x10;
            var info = new ShellFileInfo();
            // A synthetic name and USEFILEATTRIBUTES avoid touching a missing,
            // deleted, executable or network file while still using its association.
            var safeExtension = extension.Length <= 64 && extension.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-')
                ? extension : string.Empty;
            SHGetFileInfo(directory ? "folder" : "file" + safeExtension, directory ? 0x10u : 0x80u,
                ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), icon | useFileAttributes | (size <= 16 ? smallIcon : 0));
            if (info.Icon == IntPtr.Zero)
                return SystemIcons.Application.ToBitmap();
            try
            {
                using var source = Icon.FromHandle(info.Icon);
                return source.ToBitmap();
            }
            finally
            {
                DestroyIcon(info.Icon);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShellFileInfo
        {
            internal IntPtr Icon;
            internal int IconIndex;
            internal uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] internal string TypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr icon);
    }
}

/// <summary>Small, high contrast change badges shared by the tree and totals.</summary>
internal static class LauncherUpdateStatusGlyph
{
    internal static void DrawMixed(Graphics graphics, Rectangle bounds, Color foreground, Color background)
    {
        var size = Math.Max(8, bounds.Width * 3 / 4);
        Draw(graphics, new Rectangle(bounds.Left, bounds.Top, size, size), LauncherUpdateChangeKind.Added, foreground, background);
        Draw(graphics, new Rectangle(bounds.Right - size, bounds.Bottom - size, size, size), LauncherUpdateChangeKind.Deleted, foreground, background);
    }

    internal static void Draw(Graphics graphics, Rectangle bounds, LauncherUpdateChangeKind kind, Color foreground, Color background)
    {
        var color = GetColor(kind, background);
        var previous = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var shape = Rectangle.Inflate(bounds, -1, -1);
        using var brush = new SolidBrush(color);
        graphics.FillEllipse(brush, shape);
        var ink = color.GetBrightness() > .55f ? Color.FromArgb(25, 29, 32) : Color.White;
        using var pen = new Pen(ink, Math.Max(1.5f, bounds.Width / 9f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var centerX = bounds.Left + bounds.Width / 2f;
        var centerY = bounds.Top + bounds.Height / 2f;
        var arm = bounds.Width * .22f;
        if (kind == LauncherUpdateChangeKind.Modified)
        {
            graphics.DrawLine(pen, centerX - arm, centerY + arm, centerX + arm, centerY - arm);
            using var tip = new SolidBrush(ink);
            graphics.FillEllipse(tip, centerX - arm - 1f, centerY + arm - 1f, 2.5f, 2.5f);
        }
        else
        {
            graphics.DrawLine(pen, centerX - arm, centerY, centerX + arm, centerY);
            if (kind == LauncherUpdateChangeKind.Added) graphics.DrawLine(pen, centerX, centerY - arm, centerX, centerY + arm);
        }
        graphics.Restore(previous);
    }

    internal static Color GetColor(LauncherUpdateChangeKind kind, Color background)
    {
        var dark = background.GetBrightness() < .55f;
        return kind switch
        {
            LauncherUpdateChangeKind.Added => dark ? Color.FromArgb(98, 211, 146) : Color.FromArgb(20, 115, 60),
            LauncherUpdateChangeKind.Deleted => dark ? Color.FromArgb(255, 139, 131) : Color.FromArgb(185, 39, 39),
            _ => dark ? Color.FromArgb(255, 208, 104) : Color.FromArgb(147, 92, 10)
        };
    }
}
