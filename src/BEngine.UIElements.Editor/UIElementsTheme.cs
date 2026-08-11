using DrawingColor = System.Drawing.Color;
using System.Runtime.InteropServices;

namespace BEngine.UIElements.Editor;

internal static class UIElementsTheme
{
    // Unity 2021/2022-style neutral dark surfaces. Blue is reserved for focus and selection.
    public static readonly DrawingColor Workspace = DrawingColor.FromArgb(38, 38, 38);
    public static readonly DrawingColor Window = DrawingColor.FromArgb(45, 45, 45);
    public static readonly DrawingColor Panel = DrawingColor.FromArgb(56, 56, 56);
    public static readonly DrawingColor PanelRaised = DrawingColor.FromArgb(60, 60, 60);
    public static readonly DrawingColor PanelHeader = DrawingColor.FromArgb(64, 64, 64);
    public static readonly DrawingColor Toolbar = DrawingColor.FromArgb(58, 58, 58);
    public static readonly DrawingColor TabInactive = DrawingColor.FromArgb(47, 47, 47);
    public static readonly DrawingColor TabHover = DrawingColor.FromArgb(65, 65, 65);
    public static readonly DrawingColor Field = DrawingColor.FromArgb(42, 42, 42);
    public static readonly DrawingColor FieldHover = DrawingColor.FromArgb(49, 49, 49);
    public static readonly DrawingColor FieldReadOnly = DrawingColor.FromArgb(47, 47, 47);
    public static readonly DrawingColor Border = DrawingColor.FromArgb(31, 31, 31);
    public static readonly DrawingColor Divider = DrawingColor.FromArgb(27, 27, 27);
    public static readonly DrawingColor Hover = DrawingColor.FromArgb(70, 70, 70);
    public static readonly DrawingColor RowHover = DrawingColor.FromArgb(62, 62, 62);
    public static readonly DrawingColor Selection = DrawingColor.FromArgb(45, 93, 135);
    public static readonly DrawingColor SelectionInactive = DrawingColor.FromArgb(73, 73, 73);
    public static readonly DrawingColor TextStrong = DrawingColor.FromArgb(230, 230, 230);
    public static readonly DrawingColor Text = DrawingColor.FromArgb(210, 210, 210);
    public static readonly DrawingColor TextMuted = DrawingColor.FromArgb(166, 166, 166);
    public static readonly DrawingColor TextDisabled = DrawingColor.FromArgb(128, 128, 128);
    public static readonly DrawingColor Accent = DrawingColor.FromArgb(58, 121, 178);
    public static readonly DrawingColor AccentHover = DrawingColor.FromArgb(75, 143, 202);
    public static readonly DrawingColor Error = DrawingColor.FromArgb(232, 92, 84);
    public static readonly DrawingColor Warning = DrawingColor.FromArgb(231, 183, 77);
    public static readonly DrawingColor Success = DrawingColor.FromArgb(91, 181, 120);

    public const int MenuHeight = 25;
    public const int MainToolbarHeight = 32;
    public const int ToolbarHeight = 24;
    public const int StatusHeight = 22;
    public const int ControlHeight = 22;
    public const int RowHeight = 22;
    public const int TabHeight = 24;
    public const int SplitterWidth = 4;

    public static Font Font(float size = 9f, System.Drawing.FontStyle style = System.Drawing.FontStyle.Regular) =>
        new("Microsoft YaHei UI", Math.Clamp(size, 8.5f, 20f), style, GraphicsUnit.Point);

    public static Font MonoFont(float size = 9f) =>
        new("Cascadia Mono", Math.Clamp(size, 8.5f, 20f), System.Drawing.FontStyle.Regular, GraphicsUnit.Point);

    public static BEngine.UIElements.UIColor UiColor(DrawingColor color) =>
        new(color.R, color.G, color.B, color.A);

    public static void Apply(Control control, DrawingColor? background = null)
    {
        control.BackColor = background ?? Panel;
        control.ForeColor = Text;
        control.Font = Font();
    }

    public static void ApplyDarkTitleBar(Form form)
    {
        void ApplyToHandle()
        {
            var enabled = 1;
            if (DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref enabled, sizeof(int));

            var caption = ToColorRef(PanelRaised);
            var text = ToColorRef(TextStrong);
            var border = ToColorRef(Border);
            DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int));
        }

        if (form.IsHandleCreated) ApplyToHandle();
        form.HandleCreated += (_, _) => ApplyToHandle();
    }

    public static void ApplyMenuStrip(MenuStrip menu)
    {
        ApplyToolStrip(menu, MenuHeight, new Padding(4, 0, 4, 0));
        menu.BackColor = PanelRaised;
    }

    public static void ApplyMainToolbar(ToolStrip toolbar) =>
        ApplyToolStrip(toolbar, MainToolbarHeight, new Padding(6, 3, 6, 3));

    public static void ApplyCompactToolbar(ToolStrip toolbar) =>
        ApplyToolStrip(toolbar, ToolbarHeight, new Padding(4, 2, 4, 2));

    public static void ApplyStatusStrip(StatusStrip status)
    {
        ApplyToolStrip(status, StatusHeight, new Padding(5, 1, 5, 1));
        status.BackColor = PanelRaised;
        status.SizingGrip = false;
    }

    public static void ApplySplitContainer(SplitContainer split)
    {
        split.BackColor = Divider;
        split.SplitterWidth = SplitterWidth;
        split.Panel1.BackColor = Panel;
        split.Panel2.BackColor = Panel;
    }

    public static void ApplyContextMenu(ContextMenuStrip menu)
    {
        menu.BackColor = PanelRaised;
        menu.ForeColor = Text;
        menu.Font = Font();
        menu.Renderer = CreateToolStripRenderer();
        menu.ShowImageMargin = false;
        menu.Padding = new Padding(1);
    }

    public static ToolStripProfessionalRenderer CreateToolStripRenderer() => new(new DarkColorTable())
    {
        RoundedEdges = false
    };

    public static System.Windows.Forms.Button Button(string text)
    {
        var button = new System.Windows.Forms.Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(28, ControlHeight),
            BackColor = PanelRaised,
            ForeColor = Text,
            FlatStyle = FlatStyle.Flat,
            Font = Font(),
            Margin = new Padding(2, 0, 2, 0),
            Padding = new Padding(6, 0, 6, 0),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Hover;
        button.FlatAppearance.MouseDownBackColor = Selection;
        return button;
    }

    private static void ApplyToolStrip(ToolStrip strip, int height, Padding padding)
    {
        strip.AutoSize = false;
        strip.Height = height;
        strip.GripStyle = ToolStripGripStyle.Hidden;
        strip.BackColor = Toolbar;
        strip.ForeColor = Text;
        strip.Font = Font();
        strip.Renderer = CreateToolStripRenderer();
        strip.Padding = padding;
        strip.ImageScalingSize = new Size(16, 16);
    }

    private static int ToColorRef(DrawingColor color) => color.R | color.G << 8 | color.B << 16;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

internal sealed class DarkColorTable : ProfessionalColorTable
{
    public override DrawingColor ToolStripGradientBegin => UIElementsTheme.Toolbar;
    public override DrawingColor ToolStripGradientMiddle => UIElementsTheme.Toolbar;
    public override DrawingColor ToolStripGradientEnd => UIElementsTheme.Toolbar;
    public override DrawingColor ToolStripBorder => UIElementsTheme.Divider;
    public override DrawingColor MenuStripGradientBegin => UIElementsTheme.PanelRaised;
    public override DrawingColor MenuStripGradientEnd => UIElementsTheme.PanelRaised;
    public override DrawingColor StatusStripGradientBegin => UIElementsTheme.PanelRaised;
    public override DrawingColor StatusStripGradientEnd => UIElementsTheme.PanelRaised;
    public override DrawingColor MenuItemSelected => UIElementsTheme.Selection;
    public override DrawingColor MenuItemSelectedGradientBegin => UIElementsTheme.Selection;
    public override DrawingColor MenuItemSelectedGradientEnd => UIElementsTheme.Selection;
    public override DrawingColor MenuItemPressedGradientBegin => UIElementsTheme.Selection;
    public override DrawingColor MenuItemPressedGradientMiddle => UIElementsTheme.Selection;
    public override DrawingColor MenuItemPressedGradientEnd => UIElementsTheme.Selection;
    public override DrawingColor MenuItemBorder => UIElementsTheme.AccentHover;
    public override DrawingColor MenuBorder => UIElementsTheme.Border;
    public override DrawingColor ButtonSelectedGradientBegin => UIElementsTheme.Hover;
    public override DrawingColor ButtonSelectedGradientMiddle => UIElementsTheme.Hover;
    public override DrawingColor ButtonSelectedGradientEnd => UIElementsTheme.Hover;
    public override DrawingColor ButtonSelectedBorder => UIElementsTheme.Border;
    public override DrawingColor ButtonPressedGradientBegin => UIElementsTheme.Selection;
    public override DrawingColor ButtonPressedGradientMiddle => UIElementsTheme.Selection;
    public override DrawingColor ButtonPressedGradientEnd => UIElementsTheme.Selection;
    public override DrawingColor ButtonPressedBorder => UIElementsTheme.AccentHover;
    public override DrawingColor ButtonCheckedGradientBegin => UIElementsTheme.Selection;
    public override DrawingColor ButtonCheckedGradientMiddle => UIElementsTheme.Selection;
    public override DrawingColor ButtonCheckedGradientEnd => UIElementsTheme.Selection;
    public override DrawingColor CheckBackground => UIElementsTheme.Selection;
    public override DrawingColor CheckSelectedBackground => UIElementsTheme.Selection;
    public override DrawingColor CheckPressedBackground => UIElementsTheme.Accent;
    public override DrawingColor ToolStripDropDownBackground => UIElementsTheme.PanelRaised;
    public override DrawingColor ImageMarginGradientBegin => UIElementsTheme.PanelRaised;
    public override DrawingColor ImageMarginGradientMiddle => UIElementsTheme.PanelRaised;
    public override DrawingColor ImageMarginGradientEnd => UIElementsTheme.PanelRaised;
    public override DrawingColor SeparatorDark => UIElementsTheme.Divider;
    public override DrawingColor SeparatorLight => UIElementsTheme.PanelHeader;
    public override DrawingColor GripDark => UIElementsTheme.Border;
    public override DrawingColor GripLight => UIElementsTheme.PanelHeader;
}
