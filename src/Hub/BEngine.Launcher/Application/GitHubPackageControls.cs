namespace BEngine.Launcher;

internal sealed class GitHubPackageListView : ListView
{
    internal GitHubPackageListView(LauncherPalette palette)
    {
        View = View.Details; OwnerDraw = true; FullRowSelect = true; HideSelection = false; MultiSelect = false;
        BackColor = palette.Raised; ForeColor = palette.Primary; BorderStyle = BorderStyle.FixedSingle;
        ShowItemToolTips = true; DoubleBuffered = true;
        DrawColumnHeader += (_, args) =>
        {
            using var background = new SolidBrush(palette.Field); args.Graphics.FillRectangle(background, args.Bounds);
            TextRenderer.DrawText(args.Graphics, args.Header?.Text, Font, Rectangle.Inflate(args.Bounds, -8, 0), palette.Primary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using var border = new Pen(palette.Border); args.Graphics.DrawLine(border, args.Bounds.Left, args.Bounds.Bottom - 1, args.Bounds.Right, args.Bounds.Bottom - 1);
        };
        DrawSubItem += (_, args) =>
        {
            var selected = args.Item?.Selected == true;
            using var background = new SolidBrush(selected ? palette.Accent : palette.Raised);
            args.Graphics.FillRectangle(background, args.Bounds);
            var bounds = Rectangle.Inflate(args.Bounds, -8, 0);
            if (args.ColumnIndex == 0 && CheckBoxes)
            {
                var box = new Rectangle(bounds.Left, bounds.Top + (bounds.Height - 14) / 2, 14, 14);
                using var fill = new SolidBrush(palette.Window); args.Graphics.FillRectangle(fill, box);
                using var border = new Pen(palette.Secondary); args.Graphics.DrawRectangle(border, box);
                if (args.Item?.Checked == true)
                {
                    using var tick = new Pen(palette.AccentText, 2);
                    args.Graphics.DrawLines(tick, [new(box.Left + 3, box.Top + 7), new(box.Left + 6, box.Top + 10), new(box.Left + 12, box.Top + 3)]);
                }
                bounds.X += 23; bounds.Width -= 23;
            }
            TextRenderer.DrawText(args.Graphics, args.SubItem?.Text, Font, bounds, selected ? System.Drawing.Color.White : args.SubItem?.ForeColor ?? palette.Primary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
    }
}

internal sealed class GitHubPackageProgressBar : Control
{
    private readonly LauncherPalette _palette;
    private double? _percent = 0;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal double? Percent { get => _percent; set { _percent = value; Invalidate(); } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int Animation { get; set; }
    internal GitHubPackageProgressBar(LauncherPalette palette)
    { _palette = palette; DoubleBuffered = true; AccessibleRole = AccessibleRole.ProgressBar; }
    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new SolidBrush(_palette.Field); e.Graphics.FillRectangle(background, ClientRectangle);
        using var fill = new SolidBrush(_palette.Accent);
        var width = Percent is { } percent ? (int)(Width * Math.Clamp(percent / 100, 0, 1)) : Width / 4;
        var left = Percent is null ? (int)((Animation / 160d) * (Width + width)) - width : 0;
        e.Graphics.FillRectangle(fill, left, 0, width, Height);
        using var border = new Pen(_palette.Border); e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
    }
}
