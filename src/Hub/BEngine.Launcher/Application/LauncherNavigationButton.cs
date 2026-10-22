namespace BEngine.Launcher;

internal sealed class LauncherNavigationButton : Button
{
    internal Color DisabledTextColor
    {
        get
        {
            if (ContrastRatio(ForeColor, BackColor) >= 4.5) return ForeColor;
            return ContrastRatio(Color.White, BackColor) >= ContrastRatio(Color.Black, BackColor)
                ? Color.White : Color.Black;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Enabled)
        {
            base.OnPaint(e);
            return;
        }

        // WinForms' disabled button renderer ignores ForeColor and can draw
        // almost black text on a dark sidebar. Keep the real disabled state,
        // but draw its label with the current theme's readable text color.
        using var background = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(background, ClientRectangle);
        var textBounds = new Rectangle(Padding.Left + 2, Padding.Top,
            Math.Max(0, ClientSize.Width - Padding.Horizontal - 4),
            Math.Max(0, ClientSize.Height - Padding.Vertical));
        var flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping;
        flags |= TextAlign switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => TextFormatFlags.HorizontalCenter,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
            _ => TextFormatFlags.Left
        };
        if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
        if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, DisabledTextColor, flags);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    private static double ContrastRatio(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte value)
            {
                var component = value / 255d;
                return component <= 0.04045 ? component / 12.92 : Math.Pow((component + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }

        var firstLuminance = Luminance(first);
        var secondLuminance = Luminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
            (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }
}
