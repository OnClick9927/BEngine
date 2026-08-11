using BEngine.UIElements.Editor;

namespace BEngine.Editor;

internal sealed class EditorTrayIcon : IDisposable
{
    private readonly ContextMenuStrip _menu;
    private readonly NotifyIcon _icon;
    private bool _notified;

    public EditorTrayIcon(
        Icon icon,
        string projectName,
        Action showEditor,
        Action showStatus,
        Action exit)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentNullException.ThrowIfNull(showEditor);
        ArgumentNullException.ThrowIfNull(showStatus);
        ArgumentNullException.ThrowIfNull(exit);

        _menu = new ContextMenuStrip
        {
            BackColor = UIElementsTheme.PanelRaised,
            ForeColor = UIElementsTheme.Text,
            Renderer = new ToolStripProfessionalRenderer(new DarkColorTable())
        };
        _menu.Items.Add("显示 BEngine", null, (_, _) => showEditor());
        _menu.Items.Add("Editor 状态", null, (_, _) => showStatus());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => exit());

        var tooltip = $"BEngine - {projectName}";
        _icon = new NotifyIcon
        {
            Icon = icon,
            Text = tooltip.Length <= 63 ? tooltip : tooltip[..63],
            ContextMenuStrip = _menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => showEditor();
    }

    public void NotifyMinimized()
    {
        if (_notified) return;
        _notified = true;
        _icon.ShowBalloonTip(2500, "BEngine", "编辑器已最小化到托盘。", ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
