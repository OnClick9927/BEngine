using System.Diagnostics;
using System.Windows.Forms;

namespace BEngine.Editor;

internal sealed class WindowsEditorUtilityPlatform : IEditorUtilityPlatform
{
    private EditorUtilityProgressDialog? _progressDialog;

    public int DisplayDialog(string title, string message, IReadOnlyList<string> buttons)
    {
        using var dialog = new EditorUtilityDialog(title, message, buttons);
        var ownerHandle = GetActiveWindow();
        if (ownerHandle == IntPtr.Zero)
        {
            dialog.ShowDialog();
        }
        else
        {
            var owner = new NativeWindowOwner(ownerHandle);
            dialog.ShowDialog(owner);
        }
        return dialog.SelectedButton;
    }

    public string OpenFilePanel(string title, string directory, string filter)
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            InitialDirectory = ResolveInitialDirectory(directory),
            Filter = filter,
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };
        return ShowDialog(dialog) == DialogResult.OK ? dialog.FileName : string.Empty;
    }

    public string OpenFolderPanel(string title, string folder, string defaultName)
    {
        var initialDirectory = ResolveInitialDirectory(folder);
        if (!string.IsNullOrWhiteSpace(defaultName))
        {
            var candidate = Path.Combine(initialDirectory, defaultName.Trim());
            if (Directory.Exists(candidate)) initialDirectory = candidate;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = title,
            InitialDirectory = initialDirectory,
            SelectedPath = initialDirectory,
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true
        };
        return ShowDialog(dialog) == DialogResult.OK ? dialog.SelectedPath : string.Empty;
    }

    public void OpenWithDefaultApp(string target)
    {
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException($"Windows could not open '{target}'.");
    }

    public void ShowProgress(EditorProgressInfo progress, Action? requestCancellation)
    {
        if (_progressDialog is null || _progressDialog.IsDisposed)
        {
            var dialog = new EditorUtilityProgressDialog();
            dialog.FormClosed += (_, _) =>
            {
                if (ReferenceEquals(_progressDialog, dialog)) _progressDialog = null;
            };
            _progressDialog = dialog;
        }

        _progressDialog.UpdateProgress(progress, requestCancellation);
        if (!_progressDialog.Visible)
        {
            var ownerHandle = GetActiveWindow();
            if (ownerHandle == IntPtr.Zero)
                _progressDialog.Show();
            else
                _progressDialog.Show(new NativeWindowOwner(ownerHandle));
        }

        _progressDialog.Refresh();
        System.Windows.Forms.Application.DoEvents();
    }

    public void ClearProgress()
    {
        var dialog = _progressDialog;
        _progressDialog = null;
        if (dialog is null || dialog.IsDisposed) return;
        dialog.Close();
        dialog.Dispose();
        System.Windows.Forms.Application.DoEvents();
    }

    private static DialogResult ShowDialog(CommonDialog dialog)
    {
        var ownerHandle = GetActiveWindow();
        return ownerHandle == IntPtr.Zero
            ? dialog.ShowDialog()
            : dialog.ShowDialog(new NativeWindowOwner(ownerHandle));
    }

    private static string ResolveInitialDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return Environment.CurrentDirectory;
        var fullPath = Path.GetFullPath(directory.Trim());
        if (File.Exists(fullPath)) return Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        return Directory.Exists(fullPath) ? fullPath : Environment.CurrentDirectory;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    private sealed class NativeWindowOwner(IntPtr handle) : IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }

    private sealed class EditorUtilityDialog : Form
    {
        internal int SelectedButton { get; private set; }

        internal EditorUtilityDialog(string title, string message, IReadOnlyList<string> buttons)
        {
            SelectedButton = buttons.Count > 1 ? 1 : 0;
            Text = title;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new System.Drawing.Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            var measured = TextRenderer.MeasureText(message, Font,
                new System.Drawing.Size(640, int.MaxValue), TextFormatFlags.WordBreak);
            ClientSize = new System.Drawing.Size(
                Math.Clamp(measured.Width + 48, 360, 720),
                Math.Clamp(measured.Height + 106, 170, 600));

            var messageLabel = new Label
            {
                Text = message,
                AutoSize = false,
                Location = new System.Drawing.Point(20, 18),
                Size = new System.Drawing.Size(ClientSize.Width - 40, ClientSize.Height - 82),
                TextAlign = System.Drawing.ContentAlignment.TopLeft
            };
            Controls.Add(messageLabel);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                Padding = new Padding(10),
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            Controls.Add(buttonPanel);

            for (var index = buttons.Count - 1; index >= 0; index--)
            {
                var captured = index;
                var button = new Button
                {
                    Text = buttons[index],
                    AutoSize = true,
                    MinimumSize = new System.Drawing.Size(88, 30),
                    Margin = new Padding(6, 0, 0, 0)
                };
                button.Click += (_, _) =>
                {
                    SelectedButton = captured;
                    DialogResult = DialogResult.OK;
                    Close();
                };
                buttonPanel.Controls.Add(button);
                if (captured == 0) AcceptButton = button;
                if (captured == 1) CancelButton = button;
            }
        }
    }

    private sealed class EditorUtilityProgressDialog : Form
    {
        private readonly Label _infoLabel;
        private readonly Label _percentLabel;
        private readonly ProgressBar _progressBar;
        private readonly Button _cancelButton;
        private Action? _requestCancellation;

        internal EditorUtilityProgressDialog()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new System.Drawing.Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new System.Drawing.Size(520, 142);

            _infoLabel = new Label
            {
                AutoEllipsis = true,
                Location = new System.Drawing.Point(18, 16),
                Size = new System.Drawing.Size(484, 24),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };
            Controls.Add(_infoLabel);

            _progressBar = new ProgressBar
            {
                Location = new System.Drawing.Point(18, 52),
                Size = new System.Drawing.Size(402, 24),
                Minimum = 0,
                Maximum = 1000,
                Style = ProgressBarStyle.Continuous
            };
            Controls.Add(_progressBar);

            _percentLabel = new Label
            {
                Location = new System.Drawing.Point(428, 52),
                Size = new System.Drawing.Size(74, 24),
                TextAlign = System.Drawing.ContentAlignment.MiddleRight
            };
            Controls.Add(_percentLabel);

            _cancelButton = new Button
            {
                Text = "Cancel",
                Location = new System.Drawing.Point(414, 94),
                Size = new System.Drawing.Size(88, 30)
            };
            _cancelButton.Click += (_, _) =>
            {
                if (_requestCancellation is null) return;
                _cancelButton.Enabled = false;
                _cancelButton.Text = "Canceling...";
                _requestCancellation();
            };
            Controls.Add(_cancelButton);
        }

        internal void UpdateProgress(EditorProgressInfo progress, Action? requestCancellation)
        {
            Text = progress.Title;
            _infoLabel.Text = progress.Info;
            _progressBar.Value = Math.Clamp((int)Math.Round(progress.Progress * 1000), 0, 1000);
            _percentLabel.Text = $"{progress.Progress:P0}";
            _requestCancellation = progress.IsCancelable ? requestCancellation : null;
            _cancelButton.Visible = progress.IsCancelable;
            _cancelButton.Enabled = progress.IsCancelable && !progress.IsCancellationRequested;
            _cancelButton.Text = progress.IsCancellationRequested ? "Canceling..." : "Cancel";
        }
    }
}
