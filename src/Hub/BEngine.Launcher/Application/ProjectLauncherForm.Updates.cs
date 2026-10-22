using DrawingFont = System.Drawing.Font;

namespace BEngine.Launcher;

internal sealed partial class ProjectLauncherForm
{
    private readonly Panel _updatesPage = new();
    private readonly Button _updatesNavigation = new();
    private readonly Button _checkUpdateButton = new();
    private readonly Button _installUpdateButton = new();
    private readonly Button _cancelUpdateButton = new();
    private readonly Button _recoverUpdateButton = new();
    private readonly Label _updateStage = new();
    private readonly Label _updateDetail = new();
    private readonly Label _updateSummary = new();
    private readonly Label _updateInstallation = new();
    private readonly ProgressBar _updateProgress = new();
    private readonly TextBox _updateCommit = new();
    private readonly ListView _updateChanges = new();
    private readonly LauncherUpdateService _updateService = new();
    private readonly System.Windows.Forms.Timer _updateCompletionTimer = new() { Interval = 500 };
    private CancellationTokenSource? _updateCancellation;
    private LauncherUpdatePlan? _pendingUpdate;
    private bool _startingUpdateInstaller;
    private bool _needsUpdateRecovery;

    private void BuildUpdatesPage()
    {
        _updatesPage.Dock = DockStyle.Fill;
        _updatesPage.BackColor = PanelBackground;
        _updatesPage.AutoScroll = true;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10, MinimumSize = new Size(0, 520),
            BackColor = PanelBackground, Margin = Padding.Empty, Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 43, 45, 48, 28, 18, 32, 40, 61 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
        _updatesPage.Controls.Add(layout);

        var intro = new Label
        {
            Text = "Check the main branch for Hub, engine and package updates. Review changes before installing.",
            Dock = DockStyle.Fill, ForeColor = SecondaryText, AutoEllipsis = true, Margin = Padding.Empty
        };
        layout.Controls.Add(intro, 0, 0);
        _updateInstallation.Dock = DockStyle.Fill;
        _updateInstallation.ForeColor = SecondaryText;
        _updateInstallation.AutoEllipsis = true;
        _updateInstallation.Margin = Padding.Empty;
        layout.Controls.Add(_updateInstallation, 0, 1);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        _checkUpdateButton.Text = "Check for updates";
        _installUpdateButton.Text = "Confirm update and restart";
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
            actions.Controls.Add(button);
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
        layout.Controls.Add(actions, 0, 2);

        _updateStage.Dock = DockStyle.Fill;
        _updateStage.Font = new DrawingFont("Segoe UI Semibold", 11f);
        _updateStage.ForeColor = PrimaryText;
        _updateStage.Text = "Ready to check for updates";
        _updateStage.Margin = Padding.Empty;
        layout.Controls.Add(_updateStage, 0, 3);
        _updateProgress.Dock = DockStyle.Fill;
        _updateProgress.Maximum = 1000;
        _updateProgress.Margin = new Padding(0, 0, 0, 3);
        layout.Controls.Add(_updateProgress, 0, 4);
        _updateDetail.Dock = DockStyle.Fill;
        _updateDetail.ForeColor = SecondaryText;
        _updateDetail.AutoEllipsis = true;
        _updateDetail.Margin = new Padding(0, 5, 0, 0);
        layout.Controls.Add(_updateDetail, 0, 5);
        _updateSummary.Dock = DockStyle.Fill;
        _updateSummary.ForeColor = PrimaryText;
        _updateSummary.Text = "The file list and update details will appear here after checking.";
        _updateSummary.Margin = Padding.Empty;
        layout.Controls.Add(_updateSummary, 0, 6);

        _updateCommit.Dock = DockStyle.Fill;
        _updateCommit.Multiline = true;
        _updateCommit.ReadOnly = true;
        _updateCommit.ScrollBars = ScrollBars.Vertical;
        _updateCommit.BorderStyle = BorderStyle.FixedSingle;
        _updateCommit.BackColor = FieldBackground;
        _updateCommit.ForeColor = SecondaryText;
        _updateCommit.Margin = new Padding(0, 0, 0, 10);
        _updateCommit.AccessibleName = "Update details";
        layout.Controls.Add(_updateCommit, 0, 7);

        _updateChanges.Dock = DockStyle.Fill;
        _updateChanges.View = View.Details;
        _updateChanges.FullRowSelect = true;
        _updateChanges.HideSelection = false;
        _updateChanges.ShowItemToolTips = true;
        _updateChanges.MultiSelect = false;
        _updateChanges.BackColor = FieldBackground;
        _updateChanges.ForeColor = PrimaryText;
        _updateChanges.BorderStyle = BorderStyle.FixedSingle;
        _updateChanges.Columns.Add("Change", 85);
        _updateChanges.Columns.Add("File", 480);
        _updateChanges.Columns.Add("Download size", 110);
        _updateChanges.AccessibleName = "Update file changes";
        _updateChanges.Resize += (_, _) => ResizeUpdateColumns();
        layout.Controls.Add(_updateChanges, 0, 8);
        layout.Controls.Add(new Label
        {
            Text = "After confirmation, this installation's BEngine processes will close automatically. Save your work first. Preferences and project history are kept.",
            Dock = DockStyle.Fill, ForeColor = WarningText, Margin = new Padding(0, 9, 0, 0), AutoEllipsis = true
        }, 0, 9);
        FormClosing += (_, _) => _updateCancellation?.Cancel();
        _updateCompletionTimer.Tick += (_, _) => InitializeUpdateRecovery();
        FormClosed += (_, _) =>
        {
            _updateCompletionTimer.Stop();
            _updateCompletionTimer.Dispose();
            _updateService.Dispose();
        };
    }

    private void ShowUpdatesPage()
    {
        ShowPage(_updatesPage, "Updates", _updatesNavigation);
        SetLocalizedText(_updateInstallation, "Installation: {0}", LauncherInstallationPaths.ResolveRoot());
        SetLocalizedText(_statusLabel, "Update source: OnClick9927/BEngine · main / Engine");
        ResizeUpdateColumns();
    }

    private void InitializeUpdateRecovery()
    {
        var installationRoot = LauncherInstallationPaths.ResolveRoot();
        if (!LauncherUpdateInstaller.HasPendingUpdate(installationRoot))
        {
            _updateCompletionTimer.Stop();
            if (!_needsUpdateRecovery) return;
            _needsUpdateRecovery = false;
            _recoverUpdateButton.Visible = false;
            _installUpdateButton.Visible = true;
            _cancelUpdateButton.Visible = true;
            SetUpdateBusy(false);
            _updateProgress.Value = 1000;
            _updateDetail.Text = string.Empty;
            SetLocalizedText(_updateStage, "The update has completed. Hub is ready.");
            SetLocalizedText(_updateSummary, "You can open a project or check for further updates.");
            return;
        }
        _needsUpdateRecovery = true;
        ShowUpdatesPage();
        _installUpdateButton.Visible = false;
        _cancelUpdateButton.Visible = false;
        SetUpdateBusy(false);
        if (LauncherUpdateInstaller.IsUpdateInProgress(installationRoot))
        {
            _recoverUpdateButton.Visible = false;
            SetLocalizedText(_updateStage, "Finishing the current update...");
            SetLocalizedText(_updateSummary, "The updater is checking this Hub before completing the installation.");
            SetLocalizedText(_updateDetail, "Please wait for the updater to finish.");
            _updateProgress.Style = ProgressBarStyle.Marquee;
            _updateCompletionTimer.Start();
            return;
        }
        _updateCompletionTimer.Stop();
        _recoverUpdateButton.Visible = true;
        SetLocalizedText(_updateStage, "An interrupted update needs recovery");
        SetLocalizedText(_updateSummary, "Restore the previous installation from its local backup before checking for another update.");
        SetLocalizedText(_updateDetail, "Recovery does not need a network connection. Hub will close and restart.");
    }

    private void ResizeUpdateColumns()
    {
        if (_updateChanges.Columns.Count != 3) return;
        _updateChanges.Columns[0].Width = 82;
        _updateChanges.Columns[2].Width = 115;
        _updateChanges.Columns[1].Width = Math.Max(150,
            _updateChanges.ClientSize.Width - 197 - SystemInformation.VerticalScrollBarWidth - 4);
    }

    private void SetUpdateBusy(bool busy)
    {
        _checkUpdateButton.Enabled = !busy && !_needsUpdateRecovery;
        _installUpdateButton.Enabled = !busy && !_needsUpdateRecovery && _pendingUpdate?.HasUpdates == true;
        _cancelUpdateButton.Enabled = busy;
        _recoverUpdateButton.Enabled = !busy;
        _projectsNavigation.Enabled = !busy && !_needsUpdateRecovery;
        _enginesNavigation.Enabled = !busy && !_needsUpdateRecovery;
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
        var amount = progress.Total <= 0 ? string.Empty : progress.Stage == "Downloading"
            ? $"{FormatUpdateBytes(progress.Completed)} / {FormatUpdateBytes(progress.Total)} · "
            : $"{progress.Completed:N0} / {progress.Total:N0} · ";
        var percentage = progress.Percent is { } value ? $"{value:N1}% · " : string.Empty;
        _updateDetail.Text = percentage + amount + T(progress.Message);
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_updateCancellation is not null || _startingUpdateInstaller || _needsUpdateRecovery) return;
        _pendingUpdate = null;
        _updateChanges.Items.Clear();
        _updateCommit.Clear();
        _updateDetail.Text = string.Empty;
        _updateProgress.Value = 0;
        _updateDetail.ForeColor = SecondaryText;
        SetLocalizedText(_updateSummary, "Checking the remote snapshot and local installation...");
        using var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        SetUpdateBusy(true);
        try
        {
            var plan = await _updateService.CheckAsync(LauncherInstallationPaths.ResolveRoot(),
                new Progress<LauncherUpdateProgress>(ReportUpdateProgress), cancellation.Token);
            if (IsDisposed || Disposing) return;
            _pendingUpdate = plan;
            RenderUpdatePreview(plan);
            _updateProgress.Style = ProgressBarStyle.Continuous;
            _updateProgress.Value = 1000;
            SetLocalizedText(_updateStage, plan.HasUpdates ? "Updates available. Review changes, then confirm." : "This installation is up to date.");
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed) SetLocalizedText(_updateStage, "Update check cancelled. No files were changed.");
        }
        catch (Exception exception)
        {
            if (!IsDisposed)
            {
                SetLocalizedText(_updateStage, "Could not check for updates");
                _updateDetail.Text = T(exception.Message);
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
        var added = plan.Changes.Count(change => change.Kind == LauncherUpdateChangeKind.Added);
        var modified = plan.Changes.Count(change => change.Kind == LauncherUpdateChangeKind.Modified);
        var deleted = plan.Changes.Count(change => change.Kind == LauncherUpdateChangeKind.Deleted);
        SetLocalizedText(_updateSummary, "{0} added · {1} modified · {2} removed · download {3}", added, modified, deleted, FormatUpdateBytes(plan.DownloadBytes));
        var message = plan.CommitMessage
            .Replace("Recent Engine changes (not a complete changelog):", T("Recent Engine changes (not a complete changelog):"), StringComparison.Ordinal)
            .Replace("Snapshot commit: ", T("Snapshot commit: "), StringComparison.Ordinal);
        _updateCommit.Text = $"GitHub main · {plan.CommitSha[..Math.Min(12, plan.CommitSha.Length)]}{Environment.NewLine}{message}";
        _updateChanges.BeginUpdate();
        try
        {
            _updateChanges.Items.Clear();
            foreach (var change in plan.Changes)
            {
                var item = new ListViewItem(T(change.Kind.ToString()));
                item.ToolTipText = change.Path;
                item.SubItems.Add(change.Path);
                item.SubItems.Add(change.Kind == LauncherUpdateChangeKind.Deleted ? "—" : FormatUpdateBytes(change.Size));
                _updateChanges.Items.Add(item);
            }
        }
        finally { _updateChanges.EndUpdate(); }
        _updateDetail.ForeColor = SecondaryText;
        SetLocalizedText(_updateDetail, "Only the changes shown above will be installed after confirmation.");
    }

    private async Task InstallPendingUpdateAsync()
    {
        if (_updateCancellation is not null || _pendingUpdate?.HasUpdates != true || _startingUpdateInstaller || _needsUpdateRecovery) return;
        var plan = _pendingUpdate;
        using var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        SetUpdateBusy(true);
        try
        {
            await _updateService.DownloadAsync(plan, new Progress<LauncherUpdateProgress>(ReportUpdateProgress), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (IsDisposed || Disposing) return;
            _cancelUpdateButton.Enabled = false;
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
            if (!IsDisposed) SetLocalizedText(_updateStage, "Download cancelled. The installed engine was not changed.");
        }
        catch (Exception exception)
        {
            if (!IsDisposed)
            {
                SetLocalizedText(_updateStage, "Update could not start. The installed engine was not changed.");
                _updateDetail.ForeColor = ErrorText;
                _updateDetail.Text = T(exception.Message);
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
                SetLocalizedText(_updateStage, "Recovery could not start. The backup has been kept.");
                _updateDetail.ForeColor = ErrorText;
                _updateDetail.Text = T(exception.Message);
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
