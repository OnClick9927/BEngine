using System.Drawing;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;

namespace BEngine.Launcher;

internal sealed class LauncherUpdateInstallerForm : Form
{
    private readonly LauncherUpdatePlan _plan;
    private readonly bool _chinese;
    private readonly Action? _acknowledgeReady;
    private readonly bool _recoveryOnly;
    private readonly Label _stage = new();
    private readonly Label _detail = new();
    private readonly ProgressBar _progress = new();
    private readonly TextBox _result = new();
    private readonly Button _close = new();
    private bool _finished;
    internal bool Succeeded { get; private set; }

    internal LauncherUpdateInstallerForm(LauncherUpdatePlan plan, string language, Action? acknowledgeReady = null, bool recoveryOnly = false)
    {
        _plan = plan;
        _acknowledgeReady = acknowledgeReady;
        _recoveryOnly = recoveryOnly;
        _chinese = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
                   language.Equals("system", StringComparison.OrdinalIgnoreCase) && System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        Text = T("BEngine 更新", "BEngine Update");
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 360);
        MinimumSize = new Size(540, 350);
        BackColor = DrawingColor.FromArgb(30, 33, 37);
        ForeColor = DrawingColor.FromArgb(235, 238, 242);
        Font = new DrawingFont("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        _stage.Dock = DockStyle.Fill;
        _stage.Font = new DrawingFont(Font.FontFamily, 15, FontStyle.Bold);
        _stage.Text = T("正在准备安装", "Preparing installation");
        _detail.Dock = DockStyle.Fill;
        _detail.AutoEllipsis = true;
        _progress.Dock = DockStyle.Fill;
        _progress.Minimum = 0;
        _progress.Maximum = 1000;
        var warning = new Label { Dock = DockStyle.Fill, Text = T("请等待更新完成。相关 BEngine 窗口将关闭，项目和偏好设置会保留。", "Please wait. This installation's BEngine windows will close; projects and preferences are preserved."), AutoEllipsis = true, ForeColor = DrawingColor.FromArgb(183, 193, 204) };
        _result.Dock = DockStyle.Fill;
        _result.Multiline = true;
        _result.ReadOnly = true;
        _result.ScrollBars = ScrollBars.Vertical;
        _result.BackColor = BackColor;
        _result.ForeColor = ForeColor;
        _result.BorderStyle = BorderStyle.None;
        _result.Text = T("安装目录：", "Installation: ") + plan.InstallationRoot;
        _close.Dock = DockStyle.Right;
        _close.Width = 130;
        _close.Text = T("关闭", "Close");
        _close.Enabled = false;
        _close.FlatStyle = FlatStyle.Flat;
        _close.Click += (_, _) => Close();
        layout.Controls.Add(_stage, 0, 0);
        layout.Controls.Add(_detail, 0, 1);
        layout.Controls.Add(_progress, 0, 2);
        layout.Controls.Add(warning, 0, 3);
        layout.Controls.Add(_result, 0, 4);
        layout.Controls.Add(_close, 0, 5);
        Controls.Add(layout);
        FormClosing += (_, args) => args.Cancel = !_finished;
        Shown += async (_, _) => await InstallAsync();
    }

    private async Task InstallAsync()
    {
        try
        {
            _acknowledgeReady?.Invoke();
            var progress = new Progress<LauncherUpdateProgress>(value =>
            {
                _stage.Text = value.Stage switch
                {
                    "closing" => T("1/6  关闭相关 BEngine 进程", "1/6  Closing BEngine processes"),
                    "verifying" => T("2/6  校验下载内容", "2/6  Verifying downloaded files"),
                    "backup" => T("3/6  备份原有文件", "3/6  Backing up installed files"),
                    "installing" => T("4/6  安装更新", "4/6  Installing update"),
                    "verifying-installed" => T("5/6  校验安装结果", "5/6  Verifying installed files"),
                    "restarting" => T("6/6  启动新的 Hub", "6/6  Starting the updated Hub"),
                    "cleanup" => T("正在清理已完成更新的下载内容", "Removing completed update downloads"),
                    "rollback" => T("正在恢复原有安装", "Restoring the previous installation"),
                    "completed" => T("更新完成", "Update complete"),
                    _ => value.Stage
                };
                _detail.Text = value.Total > 0 ? $"{value.Completed:N0} / {value.Total:N0}  {value.Message}" : value.Message;
                _progress.Style = value.Percent.HasValue ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                if (value.Percent.HasValue) _progress.Value = (int)Math.Clamp(value.Percent.Value * 10, 0, 1000);
            });
            await Task.Run(() => LauncherUpdateInstaller.InstallAsync(_plan, progress, recoveryOnly: _recoveryOnly));
            Succeeded = true;
            _stage.Text = _recoveryOnly ? T("原有安装已恢复，Hub 已重新启动", "The previous installation has been restored. Hub has restarted.") : T("更新完成，Hub 已重新启动", "Update complete. Hub has restarted.");
            _result.Text = T("项目和偏好设置已保留。更新日志：", "Projects and preferences have been preserved. Update log: ") + Environment.NewLine + _plan.WorkspaceRoot;
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 1000;
            _finished = true;
            Close();
        }
        catch (Exception exception)
        {
            _stage.Text = T("更新未完成", "Update did not complete");
            _detail.Text = T("请查看以下原因；可以关闭此窗口后重新打开 Hub。", "See the reason below. You can close this window and open Hub again.");
            _result.Text = (_chinese ? "未完成本次更新。以下为具体原因及恢复结果：\r\n\r\n" : string.Empty) + exception.Message +
                           Environment.NewLine + Environment.NewLine + T("备份与日志：", "Backup and log: ") + _plan.WorkspaceRoot;
            _progress.Style = ProgressBarStyle.Continuous;
            _finished = true;
            _close.Enabled = true;
        }
    }

    private string T(string chinese, string english) => _chinese ? chinese : english;
}
