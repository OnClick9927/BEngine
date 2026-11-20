namespace BEngine.Launcher;

internal sealed class EngineUpdatesForm : Form
{
    internal EngineUpdatesForm(Control content, LauncherPalette palette)
    {
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(980, 720); MinimumSize = new Size(880, 640);
        Padding = new Padding(22, 16, 22, 16);
        AutoScaleMode = AutoScaleMode.Dpi; Font = new System.Drawing.Font("Segoe UI", 10f);
        BackColor = palette.Panel; ForeColor = palette.Primary;
        MinimizeBox = false; ShowInTaskbar = false;
        Icon = ProjectLauncherForm.LoadWindowIcon();
        BEngine.Tools.WindowsBrandingIcons? windowIcons = null;
        HandleCreated += (_, _) => windowIcons = new(Handle, "BEngine.Launcher");
        HandleDestroyed += (_, _) => { windowIcons?.Dispose(); windowIcons = null; };
        content.Dock = DockStyle.Fill; Controls.Add(content);
    }
}
