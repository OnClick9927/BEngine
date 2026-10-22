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
    private readonly Label _updateSummary = new();
    private readonly Label _updateInstallation = new();
    private readonly ProgressBar _updateProgress = new();
    private readonly TextBox _updateCommit = new();
    private readonly LauncherUpdateChangesView _updateChanges = new();
    private readonly LauncherUpdateStatistics _updateStatistics = new();
    private readonly LauncherUpdateService _updateService = new();
    private readonly System.Windows.Forms.Timer _updateCompletionTimer = new() { Interval = 500 };
    private CancellationTokenSource? _updateCancellation;
    private LauncherUpdatePlan? _pendingUpdate;
    private bool _startingUpdateInstaller;
    private bool _needsUpdateRecovery;
    private bool _resizingUpdateLayout;
    private bool _movingUpdateSplitter;
    private double _updateSplitRatio = 0.32;
    private UpdatePageMode _updatePageMode;
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
            _updateActions.Controls.Add(button);
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
        _updateStage.Text = "Ready to check for updates";
        _updateStage.Margin = Padding.Empty;
        layout.Controls.Add(_updateStage, 0, 2);
        _updateProgress.Dock = DockStyle.Fill;
        _updateProgress.Maximum = 1000;
        _updateProgress.Margin = new Padding(0, 0, 0, 3);
        layout.Controls.Add(_updateProgress, 0, 3);
        _updateDetail.Dock = DockStyle.Fill;
        _updateDetail.ForeColor = SecondaryText;
        _updateDetail.AutoEllipsis = true;
        _updateDetail.Margin = new Padding(0, 5, 0, 0);
        layout.Controls.Add(_updateDetail, 0, 4);
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
        foreach (var control in new Control[] { _updateStage, _updateSummary, _updateDetail })
            control.TextChanged += (_, _) => ResizeUpdateLayout();
        FormClosing += (_, _) => _updateCancellation?.Cancel();
        _updateCompletionTimer.Tick += (_, _) => InitializeUpdateRecovery();
        FormClosed += (_, _) =>
        {
            _updateCompletionTimer.Stop();
            _updateCompletionTimer.Dispose();
            _updateService.Dispose();
        };
        ApplyUpdatePageMode(UpdatePageMode.Idle);
    }

    private void ApplyUpdatePageMode(UpdatePageMode mode)
    {
        _updatePageMode = mode;
        var busy = mode is UpdatePageMode.Checking or UpdatePageMode.Downloading or UpdatePageMode.Installing;
        _checkUpdateButton.Visible = true;
        _installUpdateButton.Visible = true;
        _cancelUpdateButton.Visible = mode is UpdatePageMode.Checking or UpdatePageMode.Downloading;
        _recoverUpdateButton.Visible = mode == UpdatePageMode.Recovery;
        _updateStage.Visible = mode != UpdatePageMode.Idle && !string.IsNullOrWhiteSpace(_updateStage.Text);
        _updateProgress.Visible = busy || mode == UpdatePageMode.Waiting;
        _updateDetail.Visible = mode is not (UpdatePageMode.Idle or UpdatePageMode.Preview);
        _updateCommit.Visible = true;
        _updateChanges.Visible = true;
        _updatesPage.AutoScrollPosition = Point.Empty;
        ResizeUpdateLayout();
    }

    private void ApplyUpdateAppearance()
    {
        _updateChanges.ApplyPalette(_palette);
        _updateChanges.ReLocalize(key => T(key));
        _updateStatistics.SetPresentation(_palette, key => T(key));
        _updateSplit.BackColor = Border;
        _updateSplit.Panel1.BackColor = _updateSplit.Panel2.BackColor = PanelBackground;
        UpdateCommitHistory();
        UpdateChangeSummary();
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
            var actionHeight = new[] { _checkUpdateButton, _installUpdateButton, _cancelUpdateButton, _recoverUpdateButton }
                .Max(button => Math.Max(button.MinimumSize.Height, button.GetPreferredSize(Size.Empty).Height)) + Scale(8);
            float[] heights =
            [
                TextHeight(_updateInstallation, 1, 1, 8),
                actionHeight,
                _updatePageMode != UpdatePageMode.Idle && !string.IsNullOrWhiteSpace(_updateStage.Text)
                    ? TextHeight(_updateStage, 1, 2, 4) : 0,
                busy || _updatePageMode == UpdatePageMode.Waiting ? Scale(18) : 0,
                _updatePageMode is not (UpdatePageMode.Idle or UpdatePageMode.Preview) && !string.IsNullOrEmpty(_updateDetail.Text)
                    ? TextHeight(_updateDetail, 1, 4, 8) : 0,
                0
            ];
            var headerHeight = (int)heights.Sum();
            heights[5] = Math.Max(Scale(220), _updatesPage.ClientSize.Height - headerHeight - Scale(1));
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
        _checkUpdateButton.Enabled = !busy && !_needsUpdateRecovery;
        _installUpdateButton.Enabled = !busy && !_needsUpdateRecovery && _pendingUpdate?.HasUpdates == true;
        _cancelUpdateButton.Enabled = busy;
        _recoverUpdateButton.Enabled = !busy;
        RefreshPrimaryNavigation(!busy && !_needsUpdateRecovery);
        _settingsNavigation.Enabled = !busy && !_needsUpdateRecovery;
        _updateProgress.Style = ProgressBarStyle.Continuous;
    }

    private void ReportUpdateProgress(LauncherUpdateProgress progress)
    {
        if (IsDisposed || Disposing || _updateCancellation is null || _updateCancellation.IsCancellationRequested) return;
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
        SetLocalizedText(_updateStage, title);
        if (progress.Percent is { } percent)
        {
            _updateProgress.Style = ProgressBarStyle.Continuous;
            _updateProgress.Value = (int)Math.Clamp(percent * 10, 0, 1000);
        }
        else _updateProgress.Style = ProgressBarStyle.Marquee;
        var amount = progress.Total <= 0 ? progress.Completed > 0 ? $"{FormatUpdateBytes(progress.Completed)} · " : string.Empty : progress.Stage is "Downloading" or "Connecting" or "Listing"
            ? $"{FormatUpdateBytes(progress.Completed)} / {FormatUpdateBytes(progress.Total)} · "
            : $"{progress.Completed:N0} / {progress.Total:N0} · ";
        var percentage = progress.Percent is { } value ? $"{value:N1}% · " : string.Empty;
        _updateDetail.Text = percentage + amount + LocalizeUpdateMessage(progress.Message);
    }

    private string LocalizeUpdateMessage(string message) => string.Join(" · ", message.Split(" · ").Select(part => T(part)));

    private async Task CheckForUpdatesAsync()
    {
        if (_updateCancellation is not null || _startingUpdateInstaller || _needsUpdateRecovery) return;
        _pendingUpdate = null;
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
        SetLocalizedText(_updateStage, "Connecting to GitHub...");
        _updateProgress.Style = ProgressBarStyle.Marquee;
        try
        {
            var plan = await _updateService.CheckAsync(LauncherInstallationPaths.ResolveRoot(),
                new Progress<LauncherUpdateProgress>(ReportUpdateProgress), cancellation.Token);
            if (IsDisposed || Disposing) return;
            _pendingUpdate = plan;
            RenderUpdatePreview(plan);
            ApplyUpdatePageMode(plan.HasUpdates ? UpdatePageMode.Preview : UpdatePageMode.Result);
            _updateProgress.Style = ProgressBarStyle.Continuous;
            _updateProgress.Value = 1000;
            SetLocalizedText(_updateStage, plan.HasUpdates ? "Updates available. Review changes, then confirm." : "This installation is up to date.");
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed)
            {
                ApplyUpdatePageMode(UpdatePageMode.Result);
                SetLocalizedText(_updateStage, "Update check cancelled. No files were changed.");
                _updateDetail.Text = string.Empty;
            }
        }
        catch (Exception exception)
        {
            if (!IsDisposed)
            {
                ApplyUpdatePageMode(UpdatePageMode.Result);
                SetLocalizedText(_updateStage, "Could not check for updates");
                _updateDetail.Text = LocalizeUpdateMessage(exception.Message);
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
        UpdateChangeSummary();
        UpdateCommitHistory();
        _updateChanges.SetChanges(plan.Changes, key => T(key));
        _updateDetail.ForeColor = SecondaryText;
        _updateDetail.Text = string.Empty;
    }

    private async Task InstallPendingUpdateAsync()
    {
        if (_updateCancellation is not null || _pendingUpdate?.HasUpdates != true || _startingUpdateInstaller || _needsUpdateRecovery) return;
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
        SetLocalizedText(_updateStage, "Preparing the download...");
        _updateProgress.Style = ProgressBarStyle.Marquee;
        _updateDetail.Text = string.Empty;
        try
        {
            await _updateService.DownloadAsync(plan, new Progress<LauncherUpdateProgress>(ReportUpdateProgress), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (IsDisposed || Disposing) return;
            _cancelUpdateButton.Enabled = false;
            ApplyUpdatePageMode(UpdatePageMode.Installing);
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
                ApplyUpdatePageMode(UpdatePageMode.Preview);
                SetLocalizedText(_updateStage, "Updates available. Review changes, then confirm.");
                _updateDetail.Text = string.Empty;
            }
        }
        catch (Exception exception)
        {
            if (!IsDisposed)
            {
                ApplyUpdatePageMode(UpdatePageMode.DownloadFailed);
                SetLocalizedText(_updateStage, "Update could not start. The installed engine was not changed.");
                _updateDetail.ForeColor = ErrorText;
                _updateDetail.Text = LocalizeUpdateMessage(exception.Message);
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
                _updateDetail.Text = LocalizeUpdateMessage(exception.Message);
            }
        }
        finally
        {
            _updateCancellation = null;
            if (!IsDisposed) SetUpdateBusy(false);
        }
    }

    private static string FormatUpdateBytes(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / (1024d * 1024):N1} MB" : bytes >= 1024 ? $"{bytes / 1024d:N1} KB" : $"{bytes:N0} B";
}
