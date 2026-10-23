using DrawingFont = System.Drawing.Font;

namespace BEngine.Launcher;

internal sealed partial class ProjectLauncherForm
{
    private readonly Panel _updatesPage = new();
    private readonly TableLayoutPanel _updateLayout = new();
    private readonly FlowLayoutPanel _updateActions = new();
    private readonly SplitContainer _updateSplit = new();
    private readonly Panel _updateFilesFooter = new();
    private readonly Button _updatesNavigation = new();
    private readonly Button _checkUpdateButton = new LauncherNavigationButton();
    private readonly Button _installUpdateButton = new LauncherNavigationButton();
    private readonly Button _cancelUpdateButton = new LauncherNavigationButton();
    private readonly Button _recoverUpdateButton = new LauncherNavigationButton();
    private readonly Label _updateStage = new();
    private readonly Label _updateDetail = new();
    private readonly Label _updateFile = new();
    private readonly TableLayoutPanel _updateDetailRows = new();
    private readonly TableLayoutPanel _updateProgressRow = new();
    private readonly Label _updateSummary = new();
    private readonly Label _updateInstallation = new();
    private readonly ProgressBar _updateProgress = new();
    private readonly TextBox _updateCommit = new();
    private readonly LauncherUpdateChangesView _updateChanges = new();
    private readonly LauncherUpdateStatistics _updateStatistics = new();
    private readonly LauncherUpdateService _updateService = new();
    private readonly System.Windows.Forms.Timer _updateCompletionTimer = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _updateProgressTimer = new() { Interval = 100 };
    private readonly LauncherUpdateProgressBuffer _updateProgressBuffer = new();
    private readonly ToolTip _updateDetailToolTip = new() { AutoPopDelay = 30000, InitialDelay = 400, ReshowDelay = 100 };
    private CancellationTokenSource? _updateCancellation;
    private LauncherUpdatePlan? _pendingUpdate;
    private bool _updateHasRetainedDownload;
    private bool _startingUpdateInstaller;
    private bool _needsUpdateRecovery;
    private bool _resizingUpdateLayout;
    private bool _movingUpdateSplitter;
    private double _updateSplitRatio = 0.32;
    private UpdatePageMode _updatePageMode;
    private LauncherUpdateProgress? _updateLastProgress;
    private enum UpdatePageMode { Idle, Checking, Preview, DownloadFailed, Downloading, Installing, Result, Recovery, Waiting }

    private void BuildUpdatesPage()
    {
        _updatesPage.Dock = DockStyle.Fill;
        _updatesPage.BackColor = PanelBackground;
        _updatesPage.AutoScroll = true;
        var layout = _updateLayout;
        layout.Dock = DockStyle.Top;
        layout.ColumnCount = 1;
        layout.RowCount = 6;
        layout.BackColor = PanelBackground;
        layout.Margin = Padding.Empty;
        layout.Padding = Padding.Empty;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < layout.RowCount; row++)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        _updatesPage.Controls.Add(layout);

        _updateInstallation.Dock = DockStyle.Fill;
        _updateInstallation.ForeColor = SecondaryText;
        _updateInstallation.AutoEllipsis = true;
        _updateInstallation.Margin = Padding.Empty;
        layout.Controls.Add(_updateInstallation, 0, 0);

        _updateActions.Dock = DockStyle.Fill;
        _updateActions.WrapContents = false;
        _updateActions.Margin = Padding.Empty;
        _checkUpdateButton.Text = "Check for updates";
        _installUpdateButton.Text = "Confirm download and install";
        _cancelUpdateButton.Text = "Cancel";
        _recoverUpdateButton.Text = "Recover interrupted update";
        _recoverUpdateButton.Visible = false;
        foreach (var button in new[] { _checkUpdateButton, _installUpdateButton, _cancelUpdateButton, _recoverUpdateButton })
        {
            ConfigureButton(button, button == _installUpdateButton);
            button.AutoSize = true;
            button.MinimumSize = new Size(106, 35);
            button.Padding = new Padding(12, 0, 12, 0);
            button.Margin = new Padding(0, 0, 10, 0);
            if (button != _cancelUpdateButton) _updateActions.Controls.Add(button);
        }
        _checkUpdateButton.Click += async (_, _) => await CheckForUpdatesAsync();
        _installUpdateButton.Click += async (_, _) => await InstallPendingUpdateAsync();
        _recoverUpdateButton.Click += async (_, _) => await RecoverInterruptedUpdateAsync();
        _cancelUpdateButton.Click += (_, _) =>
        {
            _updateCancellation?.Cancel();
            _cancelUpdateButton.Enabled = false;
            SetLocalizedText(_updateStage, "Cancelling update operation...");
        };
        _installUpdateButton.Enabled = false;
        _cancelUpdateButton.Enabled = false;
        layout.Controls.Add(_updateActions, 0, 1);

        _updateStage.Dock = DockStyle.Fill;
        _updateStage.Font = new DrawingFont("Segoe UI Semibold", 11f);
        _updateStage.ForeColor = PrimaryText;
        _updateStage.AutoEllipsis = true;
        _updateStage.UseMnemonic = false;
        _updateStage.Text = "Ready to check for updates";
        _updateStage.Margin = Padding.Empty;
        layout.Controls.Add(_updateStage, 0, 2);
        _updateProgressRow.Dock = DockStyle.Fill;
        _updateProgressRow.Margin = Padding.Empty;
        _updateProgressRow.BackColor = PanelBackground;
        _updateProgressRow.ColumnCount = 2;
        _updateProgressRow.RowCount = 1;
        _updateProgressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _updateProgressRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _updateProgressRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _updateProgress.Dock = DockStyle.Fill;
        _updateProgress.Maximum = 1000;
        _updateProgressRow.Controls.Add(_updateProgress, 0, 0);
        _cancelUpdateButton.Dock = DockStyle.Fill;
        _cancelUpdateButton.Margin = new Padding(12, 0, 0, 0);
        _updateProgressRow.Controls.Add(_cancelUpdateButton, 1, 0);
        layout.Controls.Add(_updateProgressRow, 0, 3);
        _updateDetailRows.Dock = DockStyle.Fill;
        _updateDetailRows.Margin = new Padding(0, 5, 0, 3);
        _updateDetailRows.BackColor = PanelBackground;
        _updateDetailRows.ColumnCount = 1;
        _updateDetailRows.RowCount = 2;
        _updateDetailRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _updateFile.Font = new DrawingFont("Segoe UI", 9f);
        foreach (var label in new[] { _updateDetail, _updateFile })
        {
            label.Dock = DockStyle.Fill;
            label.ForeColor = SecondaryText;
            label.AutoEllipsis = true;
            label.UseMnemonic = false;
            label.Margin = Padding.Empty;
            _updateDetailRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            _updateDetailRows.Controls.Add(label, 0, _updateDetailRows.Controls.Count);
        }
        layout.Controls.Add(_updateDetailRows, 0, 4);
        _updateSummary.Dock = DockStyle.Top;
        _updateSummary.ForeColor = PrimaryText;
        _updateSummary.AutoEllipsis = true;
        _updateSummary.Margin = Padding.Empty;

        _updateSplit.Dock = DockStyle.Fill;
        _updateSplit.Margin = Padding.Empty;
        _updateSplit.BackColor = Border;
        _updateSplit.Panel1.BackColor = PanelBackground;
        _updateSplit.Panel2.BackColor = PanelBackground;
        // Set a valid initial width before applying panel minima. Dock layout
        // later keeps the user's proportion when the window or DPI changes.
        _updateSplit.Size = new Size(760, 400);
        _updateSplit.Panel1MinSize = 150;
        _updateSplit.Panel2MinSize = 280;
        _updateSplit.SplitterWidth = 7;
        _updateSplit.SplitterDistance = 240;
        _updateSplit.AccessibleName = "Update pane widths";
        _updateSplit.SplitterMoving += (_, _) => _movingUpdateSplitter = true;
        _updateSplit.SplitterMoved += (_, _) =>
        {
            if (_movingUpdateSplitter && !_resizingUpdateLayout && _updateSplit.ClientSize.Width > _updateSplit.SplitterWidth)
                _updateSplitRatio = _updateSplit.SplitterDistance /
                    (double)(_updateSplit.ClientSize.Width - _updateSplit.SplitterWidth);
            _movingUpdateSplitter = false;
        };
        _updateSplit.SizeChanged += (_, _) => ResizeUpdateLayout();
        layout.Controls.Add(_updateSplit, 0, 5);

        var commitTitle = new Label { Text = "Commit history", Dock = DockStyle.Top, Height = 28, ForeColor = PrimaryText };
        var filesTitle = new Label { Text = "File changes", Dock = DockStyle.Top, Height = 28, ForeColor = PrimaryText };
        _updateSplit.Panel1.Padding = new Padding(0, 0, 6, 0);
        _updateSplit.Panel2.Padding = new Padding(6, 0, 0, 0);

        _updateCommit.Dock = DockStyle.Fill;
        _updateCommit.Multiline = true;
        _updateCommit.ReadOnly = true;
        _updateCommit.ScrollBars = ScrollBars.Vertical;
        _updateCommit.BorderStyle = BorderStyle.FixedSingle;
        _updateCommit.BackColor = FieldBackground;
        _updateCommit.ForeColor = SecondaryText;
        _updateCommit.Margin = Padding.Empty;
        _updateCommit.AccessibleName = "Update details";
        _updateCommit.PlaceholderText = "No commits available";
        _updateCommit.Text = T("No commits available");
        _updateSplit.Panel1.Controls.Add(_updateCommit);
        _updateSplit.Panel1.Controls.Add(commitTitle);
        _updateCommit.BringToFront();

        _updateChanges.Dock = DockStyle.Fill;
        _updateChanges.AccessibleName = "Update file changes";
        _updateChanges.Margin = Padding.Empty;
        _updateFilesFooter.Dock = DockStyle.Bottom;
        _updateFilesFooter.Height = 84;
        _updateFilesFooter.Padding = new Padding(0, 8, 0, 0);
        _updateStatistics.Dock = DockStyle.Fill;
        _updateFilesFooter.Controls.Add(_updateStatistics);
        _updateFilesFooter.Controls.Add(_updateSummary);
        _updateStatistics.BringToFront();
        _updateSplit.Panel2.Controls.Add(_updateChanges);
        _updateSplit.Panel2.Controls.Add(_updateFilesFooter);
        _updateSplit.Panel2.Controls.Add(filesTitle);
        _updateChanges.BringToFront();
        ApplyUpdateAppearance();
        _updatesPage.ClientSizeChanged += (_, _) => ResizeUpdateLayout();
        _updatesPage.FontChanged += (_, _) => ResizeUpdateLayout();
        DpiChanged += (_, _) => ResizeUpdateLayout();
        _updateStage.TextChanged += (_, _) =>
        {
            _updateDetailToolTip.SetToolTip(_updateStage, _updateStage.Text);
            _updateStage.AccessibleDescription = _updateStage.Text;
            if (!HasFixedUpdateStatusRows) ResizeUpdateLayout();
        };
        _updateDetail.TextChanged += (_, _) =>
        {
            _updateDetailToolTip.SetToolTip(_updateDetail, _updateDetail.Text);
            _updateDetail.AccessibleDescription = _updateDetail.Text;
            if (!HasFixedUpdateStatusRows) ResizeUpdateLayout();
        };
        _updateFile.TextChanged += (_, _) =>
        {
            _updateDetailToolTip.SetToolTip(_updateFile, _updateFile.Text);
            _updateFile.AccessibleDescription = _updateFile.Text;
        };
        FormClosing += (_, _) => _updateCancellation?.Cancel();
        _updateCompletionTimer.Tick += (_, _) => InitializeUpdateRecovery();
        _updateProgressTimer.Tick += (_, _) =>
        {
            if (_updateProgressTimer.Enabled && (_updatePageMode is UpdatePageMode.Checking or UpdatePageMode.Downloading)
                && _updateProgressBuffer.TryTake(out var progress)) ReportUpdateProgress(progress);
        };
        FormClosed += (_, _) =>
        {
            _updateCompletionTimer.Stop();
            _updateCompletionTimer.Dispose();
            _updateProgressTimer.Stop();
            _updateProgressTimer.Dispose();
            _updateProgressBuffer.Clear();
            _updateDetailToolTip.Dispose();
            _updateService.Dispose();
        };
        ApplyUpdatePageMode(UpdatePageMode.Idle);
    }

    private void ApplyUpdatePageMode(UpdatePageMode mode)
    {
        _updatePageMode = mode;
        if (mode is not (UpdatePageMode.Checking or UpdatePageMode.Downloading)) _updateProgressBuffer.Clear();
        var busy = mode is UpdatePageMode.Checking or UpdatePageMode.Downloading or UpdatePageMode.Installing;
        _checkUpdateButton.Visible = mode is not (UpdatePageMode.Downloading or UpdatePageMode.Installing or UpdatePageMode.Waiting);
        _installUpdateButton.Visible = mode is (UpdatePageMode.Preview or UpdatePageMode.DownloadFailed) && _pendingUpdate?.HasUpdates == true;
        SetLocalizedText(_installUpdateButton, _updateHasRetainedDownload
            ? "Continue download and install" : "Confirm download and install");
        _cancelUpdateButton.Visible = mode is UpdatePageMode.Checking or UpdatePageMode.Downloading or UpdatePageMode.Installing;
        if (mode is UpdatePageMode.Installing or UpdatePageMode.Waiting) _cancelUpdateButton.Enabled = false;
        _recoverUpdateButton.Visible = mode == UpdatePageMode.Recovery;
        _updateActions.Visible = mode is not (UpdatePageMode.Downloading or UpdatePageMode.Installing or UpdatePageMode.Waiting);
        _updateStage.Visible = HasFixedUpdateStatusRows || mode != UpdatePageMode.Idle && !string.IsNullOrWhiteSpace(_updateStage.Text);
        _updateProgressRow.Visible = _updateProgress.Visible = busy || mode == UpdatePageMode.Waiting;
        _updateDetailRows.Visible = mode is not (UpdatePageMode.Idle or UpdatePageMode.Preview);
        _updateDetail.Visible = !HasFixedUpdateStatusRows;
        _updateFile.Visible = HasFixedUpdateStatusRows;
        if (!HasFixedUpdateStatusRows) _updateFile.Text = string.Empty;
        _updateSplit.Visible = mode != UpdatePageMode.Idle;
        _updateCommit.Visible = true;
        _updateChanges.Visible = true;
        ResizeUpdateLayout();
    }

    private bool HasFixedUpdateStatusRows => _updatePageMode is UpdatePageMode.Checking or UpdatePageMode.Downloading
        or UpdatePageMode.Installing or UpdatePageMode.Waiting;

    private void ApplyUpdateAppearance()
    {
        _updateChanges.ApplyPalette(_palette);
        _updateChanges.ReLocalize(key => T(key));
        _updateStatistics.SetPresentation(_palette, key => T(key));
        _updateDetailRows.BackColor = PanelBackground;
        _updateProgressRow.BackColor = PanelBackground;
        _updateSplit.BackColor = Border;
        _updateSplit.Panel1.BackColor = _updateSplit.Panel2.BackColor = PanelBackground;
        UpdateCommitHistory();
        UpdateChangeSummary();
        if (_updateLastProgress is { } progress && _updatePageMode is UpdatePageMode.Checking or UpdatePageMode.Downloading)
            ReportUpdateProgress(progress);
        ResizeUpdateLayout();
    }

    private void UpdateChangeSummary()
    {
        var changes = _pendingUpdate?.Changes ?? [];
        SetLocalizedText(_updateSummary, "{0} changes · download {1}", changes.Count, FormatUpdateBytes(_pendingUpdate?.DownloadBytes ?? 0));
        _updateStatistics.SetChanges(changes);
    }

    private void UpdateCommitHistory()
    {
        if (_pendingUpdate is not { } plan)
        {
            _updateCommit.Text = T("No commits available");
            return;
        }
        var message = plan.CommitMessage
            .Replace("Recent Engine changes (not a complete changelog):", T("Recent Engine changes (not a complete changelog):"), StringComparison.Ordinal)
            .Replace("Snapshot commit: ", T("Snapshot commit: "), StringComparison.Ordinal);
        message = string.Join(Environment.NewLine,
            message.ReplaceLineEndings("\n").Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)));
        _updateCommit.Text = $"GitHub main · {plan.CommitSha[..Math.Min(12, plan.CommitSha.Length)]}{Environment.NewLine}{message}";
    }

    private void ShowUpdatesPage()
    {
        ShowPage(_updatesPage, "Updates", _updatesNavigation);
        SetLocalizedText(_updateInstallation, "Installation: {0}", LauncherInstallationPaths.ResolveRoot());
        SetLocalizedText(_statusLabel, "Update source: OnClick9927/BEngine · main / Engine");
        ResizeUpdateLayout();
    }

    private void InitializeUpdateRecovery()
    {
        var installationRoot = LauncherInstallationPaths.ResolveRoot();
        var installationInProgress = LauncherUpdateInstaller.IsUpdateInProgress(installationRoot);
        if (!LauncherUpdateInstaller.HasPendingUpdate(installationRoot) && !installationInProgress)
        {
            _updateCompletionTimer.Stop();
            if (!_needsUpdateRecovery) return;
            _needsUpdateRecovery = false;
            ApplyUpdatePageMode(UpdatePageMode.Result);
            SetUpdateBusy(false);
            _updateProgress.Value = 1000;
            _updateDetail.Text = string.Empty;
            SetLocalizedText(_updateStage, "The update has completed. Hub is ready.");
            return;
        }
        _needsUpdateRecovery = true;
        ShowUpdatesPage();
        SetUpdateBusy(false);
        if (installationInProgress)
        {
            ApplyUpdatePageMode(UpdatePageMode.Waiting);
            SetLocalizedText(_updateStage, "Finishing the current update...");
            SetLocalizedText(_updateDetail, "Please wait for the updater to finish.");
            _updateProgress.Style = ProgressBarStyle.Marquee;
            _updateCompletionTimer.Start();
            return;
        }
        _updateCompletionTimer.Stop();
        ApplyUpdatePageMode(UpdatePageMode.Recovery);
        SetLocalizedText(_updateStage, "An interrupted update needs recovery");
        SetLocalizedText(_updateDetail, "Recovery does not need a network connection. Hub will close and restart.");
    }

    private void ResizeUpdateLayout()
    {
        if (_resizingUpdateLayout || _updateLayout.RowStyles.Count != 6) return;
        _resizingUpdateLayout = true;
        _updateLayout.SuspendLayout();
        try
        {
            int Scale(int value) => (int)Math.Ceiling(value * _updatesPage.DeviceDpi / 96d);
            var busy = _updatePageMode is UpdatePageMode.Checking or UpdatePageMode.Downloading or UpdatePageMode.Installing;
            var width = Math.Max(Scale(440), _updatesPage.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            int TextHeight(Control control, int minimumLines, int maximumLines, int padding = 0)
            {
                var lineHeight = control.Font.Height;
                var measured = TextRenderer.MeasureText(control.Text, control.Font,
                    new Size(Math.Max(1, width - Scale(8)), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                return Math.Clamp(measured.Height, minimumLines * lineHeight, maximumLines * lineHeight) + Scale(padding);
            }
            var actionHeight = new[] { _checkUpdateButton, _installUpdateButton, _recoverUpdateButton }
                .Max(button => Math.Max(button.MinimumSize.Height, button.GetPreferredSize(Size.Empty).Height)) + Scale(8);
            var progressHeight = Math.Max(Scale(35), Math.Max(_cancelUpdateButton.MinimumSize.Height,
                _cancelUpdateButton.GetPreferredSize(Size.Empty).Height));
            var progressInset = Math.Max(0, (progressHeight - Scale(18)) / 2);
            _updateProgress.Margin = new Padding(0, progressInset, 0, progressInset);
            _cancelUpdateButton.Margin = new Padding(Scale(12), 0, 0, 0);
            // Only the path is shown below the progress bar while busy. Both
            // it and the combined status/metrics heading stay on one fixed line.
            _updateDetailRows.RowStyles[0].SizeType = HasFixedUpdateStatusRows ? SizeType.Absolute : SizeType.Percent;
            _updateDetailRows.RowStyles[0].Height = HasFixedUpdateStatusRows ? 0 : 100;
            _updateDetailRows.RowStyles[1].SizeType = SizeType.Absolute;
            _updateDetailRows.RowStyles[1].Height = HasFixedUpdateStatusRows ? _updateFile.Font.Height : 0;
            float[] heights =
            [
                TextHeight(_updateInstallation, 1, 1, 8),
                _updatePageMode is UpdatePageMode.Downloading or UpdatePageMode.Installing or UpdatePageMode.Waiting ? 0 : actionHeight,
                HasFixedUpdateStatusRows ? _updateStage.Font.Height + Scale(4)
                    : _updatePageMode != UpdatePageMode.Idle && !string.IsNullOrWhiteSpace(_updateStage.Text)
                    ? TextHeight(_updateStage, 1, 2, 4) : 0,
                busy || _updatePageMode == UpdatePageMode.Waiting ? progressHeight : 0,
                HasFixedUpdateStatusRows ? _updateFile.Font.Height + Scale(8)
                    : _updatePageMode is not (UpdatePageMode.Idle or UpdatePageMode.Preview) && !string.IsNullOrEmpty(_updateDetail.Text)
                    ? TextHeight(_updateDetail, 1, 4, 8) : 0,
                0
            ];
            var headerHeight = (int)heights.Sum();
            heights[5] = _updatePageMode == UpdatePageMode.Idle ? 0
                : Math.Max(Scale(220), _updatesPage.ClientSize.Height - headerHeight - Scale(1));
            for (var row = 0; row < heights.Length; row++) _updateLayout.RowStyles[row].Height = heights[row];
            _updateLayout.Height = (int)heights.Sum();
            _updateSplit.SplitterWidth = Scale(7);
            foreach (var panel in new[] { _updateSplit.Panel1, _updateSplit.Panel2 })
                foreach (var label in panel.Controls.OfType<Label>()) label.Height = Scale(28);
            var available = _updateSplit.ClientSize.Width - _updateSplit.SplitterWidth;
            if (available >= Scale(430))
            {
                _updateSplit.Panel1MinSize = Scale(150);
                _updateSplit.Panel2MinSize = Scale(280);
                _updateSplit.SplitterDistance = Math.Clamp((int)(available * _updateSplitRatio),
                    _updateSplit.Panel1MinSize, available - _updateSplit.Panel2MinSize);
            }
            _updateSummary.Height = Math.Max(Font.Height + Scale(4), Scale(24));
            _updateFilesFooter.Height = _updateSummary.Height + _updateStatistics.PreferredSize.Height + Scale(8);
        }
        finally
        {
            _updateLayout.ResumeLayout(true);
            _resizingUpdateLayout = false;
        }
    }

    private void SetUpdateBusy(bool busy)
    {
        _updateProgressBuffer.Clear();
        if (busy) _updateProgressTimer.Start();
        else _updateProgressTimer.Stop();
        _checkUpdateButton.Enabled = !busy && !_environmentBusy && !_needsUpdateRecovery;
        _installUpdateButton.Enabled = !busy && !_environmentBusy && !_needsUpdateRecovery && _pendingUpdate?.HasUpdates == true;
        _cancelUpdateButton.Enabled = busy && _updatePageMode is UpdatePageMode.Checking or UpdatePageMode.Downloading;
        _recoverUpdateButton.Enabled = !busy;
        RefreshPrimaryNavigation(!busy && !_needsUpdateRecovery);
        _settingsNavigation.Enabled = !busy && !_needsUpdateRecovery;
        _updateProgress.Style = ProgressBarStyle.Continuous;
        _environmentCheck.Enabled = !busy && !_environmentBusy && !_needsUpdateRecovery;
        _environmentInstall.Enabled = !busy && !_environmentBusy && !_needsUpdateRecovery && _environmentSnapshot?.IsReady != true;
    }

    private void ReportUpdateProgress(LauncherUpdateProgress progress)
    {
        if (IsDisposed || Disposing || _updateCancellation is null || _updateCancellation.IsCancellationRequested) return;
        _updateLastProgress = progress;
        var title = progress.Stage switch
        {
            "Connecting" => "Connecting to GitHub...",
            "Listing" => "Reading remote file list...",
            "Comparing" => "Comparing local files...",
            "Downloading" => "Downloading update files...",
            "Verifying" => "Verifying downloaded files...",
            "Ready" => "Update files are ready",
            _ => progress.Stage
        };
        if (progress.Percent is { } percent)
        {
            _updateProgress.Style = ProgressBarStyle.Continuous;
            _updateProgress.Value = (int)Math.Clamp(percent * 10, 0, 1000);
        }
        else _updateProgress.Style = ProgressBarStyle.Marquee;
        var byteProgress = progress.Stage is "Downloading" or "Connecting" or "Listing" ||
            _updatePageMode == UpdatePageMode.Downloading && (progress.Stage is "Verifying" or "Ready") && progress.FilePath is null;
        var amount = progress.Total <= 0 ? progress.Completed > 0 ? $"{FormatUpdateBytes(progress.Completed)} · " : string.Empty : byteProgress
            ? $"{FormatUpdateBytes(progress.Completed)} / {FormatUpdateBytes(progress.Total)} · "
            : $"{progress.Completed:N0} / {progress.Total:N0} · ";
        var percentage = progress.Percent is { } value ? $"{value:N1}% · " : string.Empty;
        var metrics = (percentage + amount).TrimEnd(' ', '·');
        var separator = progress.Message.IndexOf(" · ", StringComparison.Ordinal);
        var file = progress.FilePath ?? (separator < 0 ? progress.Message : progress.Message[..separator]);
        var status = progress.Status ?? (separator < 0 ? string.Empty : progress.Message[(separator + 3)..]);
        var statusParts = status.Split(" · ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var state = statusParts.Length == 0 ? T(title) : T(statusParts[0]);
        var diagnostics = statusParts.Length <= 1 ? string.Empty : " · " + string.Join(" · ", statusParts.Skip(1).Select(part => T(part)));
        // Put transfer totals before optional server/retry details so narrow
        // windows preserve the numbers the user needs to judge progress.
        SetLocalizedText(_updateStage, "{0} · {1}{2}", state,
            string.IsNullOrEmpty(metrics) ? FormatUpdateBytes(progress.Completed) : metrics, diagnostics);
        _updateDetail.Text = string.Empty;
        _updateFile.Text = progress.FilePath is null ? T(file) : file;
    }

    private string LocalizeUpdateMessage(string message) => string.Join(" · ", message.Split(" · ").Select(part => T(part)));

    private async Task CheckForUpdatesAsync()
    {
        if (_environmentBusy || _updateCancellation is not null || _startingUpdateInstaller || _needsUpdateRecovery) return;
        _pendingUpdate = null;
        _updateHasRetainedDownload = false;
        _updateChanges.ClearChanges();
        UpdateCommitHistory();
        UpdateChangeSummary();
        _updateDetail.Text = string.Empty;
        _updateProgress.Value = 0;
        ApplyUpdatePageMode(UpdatePageMode.Checking);
        _updateDetail.ForeColor = SecondaryText;
        using var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        SetUpdateBusy(true);
        ReportUpdateProgress(new("Connecting", "Reading main branch", Status: "Waiting for response"));
        try
        {
            var plan = await _updateService.CheckAsync(LauncherInstallationPaths.ResolveRoot(),
                _updateProgressBuffer, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (IsDisposed || Disposing) return;
            _pendingUpdate = plan;
            RenderUpdatePreview(plan);
            ApplyUpdatePageMode(plan.HasUpdates ? UpdatePageMode.Preview : UpdatePageMode.Result);
            _updateProgress.Style = ProgressBarStyle.Continuous;
            _updateProgress.Value = 1000;
            SetLocalizedText(_updateStage, plan.HasUpdates
                ? _updateHasRetainedDownload ? "Updates available. Saved download data will be verified and resumed."
                    : "Updates available. Review changes, then confirm."
                : "This installation is up to date.");
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed)
            {
                ApplyUpdatePageMode(UpdatePageMode.Idle);
                _updateStage.Text = string.Empty;
                _updateDetail.Text = string.Empty;
            }
        }
        catch (Exception exception)
        {
            if (!IsDisposed)
            {
                ApplyUpdatePageMode(UpdatePageMode.Result);
                SetLocalizedText(_updateStage, "Could not check for updates");
                _updateDetail.Text = DescribeUpdateFailure(exception);
                _updateDetail.ForeColor = ErrorText;
            }
        }
        finally
        {
            _updateCancellation = null;
            if (!IsDisposed) SetUpdateBusy(false);
        }
    }

    private void RenderUpdatePreview(LauncherUpdatePlan plan)
    {
        RefreshRetainedDownloadState();
        UpdateChangeSummary();
        UpdateCommitHistory();
        _updateChanges.SetChanges(plan.Changes, key => T(key));
        _updateDetail.ForeColor = SecondaryText;
        _updateDetail.Text = string.Empty;
    }

    private void RefreshRetainedDownloadState()
    {
        try { _updateHasRetainedDownload = _pendingUpdate is { } plan && LauncherUpdateWorkspace.HasRetainedDownload(plan); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // This is only a presentation hint. The download service performs
            // the authoritative path and hash validation before reusing data.
            _updateHasRetainedDownload = false;
            System.Diagnostics.Trace.WriteLine(error);
        }
    }

    private async Task InstallPendingUpdateAsync()
    {
        if (_environmentBusy || _updateCancellation is not null || _pendingUpdate?.HasUpdates != true || _startingUpdateInstaller || _needsUpdateRecovery) return;
        var plan = _pendingUpdate;
        if (MessageBox.Show(this,
                T("After confirmation, this installation's BEngine processes will close automatically. Save your work first. Preferences and project history are kept."),
                T("Confirm download and install"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
        using var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        ApplyUpdatePageMode(UpdatePageMode.Downloading);
        _updateDetail.ForeColor = SecondaryText;
        SetUpdateBusy(true);
        ReportUpdateProgress(new("Downloading", "Downloading changed files", 0, plan.DownloadBytes,
            plan.DownloadBytes == 0 ? 100 : 0,
            Status: _updateHasRetainedDownload ? "Verifying saved download data" : "Waiting for response"));
        try
        {
            await _updateService.DownloadAsync(plan, _updateProgressBuffer, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (IsDisposed || Disposing) return;
            _cancelUpdateButton.Enabled = false;
            ApplyUpdatePageMode(UpdatePageMode.Installing);
            _updateProgress.Style = ProgressBarStyle.Continuous;
            _updateProgress.Value = 1000;
            _updateDetail.Text = string.Empty;
            _updateFile.Text = string.Empty;
            SetLocalizedText(_updateStage, "Starting the update installer...");
            var language = _localization.Culture.Name;
            var started = await Task.Run(() =>
            {
                var success = LauncherUpdateInstaller.TryStart(plan, language, out var error);
                return (success, error);
            });
            if (!started.success) throw new IOException(started.error);
            _startingUpdateInstaller = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed)
            {
                RefreshRetainedDownloadState();
                ApplyUpdatePageMode(UpdatePageMode.Preview);
                SetLocalizedText(_updateStage, _updateHasRetainedDownload
                    ? "Updates available. Saved download data will be verified and resumed."
                    : "Updates available. Review changes, then confirm.");
                _updateDetail.Text = string.Empty;
            }
        }
        catch (Exception exception)
        {
            if (!IsDisposed)
            {
                RefreshRetainedDownloadState();
                ApplyUpdatePageMode(UpdatePageMode.DownloadFailed);
                SetLocalizedText(_updateStage, _updateHasRetainedDownload
                    ? "Download interrupted. Saved data is kept; retry to continue."
                    : "Update could not start. The installed engine was not changed.");
                _updateDetail.ForeColor = ErrorText;
                _updateDetail.Text = DescribeUpdateFailure(exception);
            }
        }
        finally
        {
            _updateCancellation = null;
            if (!IsDisposed) SetUpdateBusy(false);
        }
    }

    private async Task RecoverInterruptedUpdateAsync()
    {
        if (!_needsUpdateRecovery || _updateCancellation is not null || _startingUpdateInstaller) return;
        using var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        SetUpdateBusy(true);
        ApplyUpdatePageMode(UpdatePageMode.Installing);
        try
        {
            SetLocalizedText(_updateStage, "Starting recovery...");
            var installationRoot = LauncherInstallationPaths.ResolveRoot();
            var language = _localization.Culture.Name;
            var started = await Task.Run(() =>
            {
                var success = LauncherUpdateInstaller.TryStartRecovery(installationRoot, language, out var error);
                return (success, error);
            });
            if (!started.success) throw new IOException(started.error);
            _startingUpdateInstaller = true;
            Close();
        }
        catch (Exception exception)
        {
            if (!IsDisposed)
            {
                ApplyUpdatePageMode(UpdatePageMode.Recovery);
                SetLocalizedText(_updateStage, "Recovery could not start. The backup has been kept.");
                _updateDetail.ForeColor = ErrorText;
                _updateDetail.Text = DescribeUpdateFailure(exception);
            }
        }
        finally
        {
            _updateCancellation = null;
            if (!IsDisposed) SetUpdateBusy(false);
        }
    }

    private string DescribeUpdateFailure(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var message = LocalizeUpdateMessage(current.Message);
            if (!messages.Contains(message, StringComparer.Ordinal)) messages.Add(message);
        }
        try
        {
            var path = Path.Combine(LauncherDataPaths.LogsPath, "HubUpdate.log");
            File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
            messages.Add(T("Log: {0}", path));
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine(logError);
        }
        return string.Join(Environment.NewLine, messages);
    }

    private static string FormatUpdateBytes(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / (1024d * 1024):N1} MB" : bytes >= 1024 ? $"{bytes / 1024d:N1} KB" : $"{bytes:N0} B";
}
