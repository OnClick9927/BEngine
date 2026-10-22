using System.Drawing.Drawing2D;

namespace BEngine.Launcher;

// A proportional change bar and explicit icon/count legend remain readable
// even when a category contains only one file or no files at all.
internal sealed class LauncherUpdateStatistics : Control
{
    private LauncherPalette _palette = LauncherPalette.From(new());
    private Func<string, string> _localize = key => key;
    private readonly int[] _counts = new int[3];
    private static readonly LauncherUpdateChangeKind[] Kinds =
        [LauncherUpdateChangeKind.Added, LauncherUpdateChangeKind.Modified, LauncherUpdateChangeKind.Deleted];

    internal LauncherUpdateStatistics()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }

    internal void SetChanges(IReadOnlyList<LauncherUpdateChange> changes)
    {
        Array.Clear(_counts);
        foreach (var change in changes) _counts[(int)change.Kind]++;
        RefreshDescription();
        Invalidate();
    }

    internal void SetPresentation(LauncherPalette palette, Func<string, string> localize)
    {
        _palette = palette;
        _localize = localize;
        BackColor = palette.Panel;
        ForeColor = palette.Primary;
        RefreshDescription();
        Invalidate();
    }

    private void RefreshDescription() => AccessibleName = string.Join(" · ", Kinds.Select(kind =>
        $"{_localize(kind.ToString())} {_counts[(int)kind]:N0}"));

    public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width, Scale(66));

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        var total = _counts.Sum();
        var bar = new Rectangle(0, Scale(2), ClientSize.Width, Scale(6));
        using (var background = new SolidBrush(_palette.Border)) graphics.FillRectangle(background, bar);
        var offset = 0;
        foreach (var kind in Kinds)
        {
            var count = _counts[(int)kind];
            if (total == 0 || count == 0) continue;
            var edge = (int)Math.Round(ClientSize.Width *
                Kinds.TakeWhile(value => value != kind).Append(kind).Sum(value => _counts[(int)value]) / (double)total);
            using var brush = new SolidBrush(StatusColor(kind));
            graphics.FillRectangle(brush, offset, bar.Y, Math.Max(0, edge - offset), bar.Height);
            offset = edge;
        }
        for (var index = 0; index < Kinds.Length; index++)
        {
            var kind = Kinds[index];
            var left = ClientSize.Width * index / Kinds.Length;
            var right = ClientSize.Width * (index + 1) / Kinds.Length;
            var glyph = new Rectangle(left, Scale(19), Scale(18), Scale(18));
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            LauncherUpdateStatusGlyph.Draw(graphics, glyph, kind, ForeColor, BackColor);
            TextRenderer.DrawText(graphics, _localize(kind.ToString()), Font,
                new Rectangle(glyph.Right + Scale(5), glyph.Y - Scale(1), Math.Max(0, right - glyph.Right - Scale(7)), glyph.Height + Scale(2)),
                ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(graphics, _counts[(int)kind].ToString("N0"), Font,
                new Rectangle(left, Scale(40), Math.Max(0, right - left - Scale(5)), Font.Height + Scale(2)),
                ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private Color StatusColor(LauncherUpdateChangeKind kind) => LauncherUpdateStatusGlyph.GetColor(kind, _palette.Panel);

    private int Scale(int value) => (int)Math.Ceiling(value * DeviceDpi / 96d);
}
