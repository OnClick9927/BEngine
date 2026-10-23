using System.Diagnostics;

namespace BEngine.Launcher;

internal sealed partial class ProjectLauncherForm
{
    private readonly DotNetEnvironmentService _environmentService = new(LauncherInstallationPaths.ResolveRoot());
    private readonly CancellationTokenSource _environmentLifetime = new();
    private readonly Label _environmentRuntime = new();
    private readonly Label _environmentSdk = new();
    private readonly Label _environmentMessage = new();
    private readonly ProgressBar _environmentProgress = new();
    private readonly Button _environmentCheck = new LauncherNavigationButton();
    private readonly Button _environmentInstall = new LauncherNavigationButton();
    private readonly Button _environmentCancel = new LauncherNavigationButton();
    private DotNetEnvironmentSnapshot? _environmentSnapshot;
    private Task<DotNetEnvironmentSnapshot?>? _environmentOperation;
    private CancellationTokenSource? _environmentCancellation;
    private bool _environmentBusy;
    private bool _environmentInstallerRunning;
    private DotNetEnvironmentProgress? _environmentLastProgress;

    private Control BuildEnvironmentControls()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1,
            Margin = Padding.Empty, BackColor = PanelBackground
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var label in new[] { _environmentRuntime, _environmentSdk, _environmentMessage })
        {
            label.AutoSize = true;
            label.ForeColor = SecondaryText;
            label.Margin = new Padding(0, 0, 0, 6);
            panel.Controls.Add(label);
        }
        SetLocalizedText(_environmentRuntime, "Desktop Runtime: checking...");
        SetLocalizedText(_environmentSdk, ".NET SDK: checking...");
        SetLocalizedText(_environmentMessage, "Existing compatible installations are reused. Only missing components are installed.");
        _environmentProgress.Dock = DockStyle.Fill;
        _environmentProgress.Height = 12;
        _environmentProgress.Maximum = 1000;
        _environmentProgress.Visible = false;
        _environmentProgress.Margin = new Padding(0, 4, 0, 8);
        panel.Controls.Add(_environmentProgress);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 8)
        };
        _environmentCheck.Text = "Check environment";
        _environmentInstall.Text = "Install missing components";
        _environmentCancel.Text = "Cancel";
        foreach (var button in new[] { _environmentCheck, _environmentInstall, _environmentCancel })
        {
            ConfigureButton(button, button == _environmentInstall);
            button.AutoSize = true;
            button.MinimumSize = new Size(130, 34);
            button.Padding = new Padding(12, 2, 12, 2);
            button.Margin = new Padding(0, 0, 10, 0);
            actions.Controls.Add(button);
        }
        _environmentInstall.Enabled = false;
        _environmentCancel.Visible = false;
        _environmentCheck.Click += async (_, _) => await CheckDevelopmentEnvironmentAsync();
        _environmentInstall.Click += async (_, _) => await InstallDevelopmentEnvironmentAsync();
        _environmentCancel.Click += (_, _) => _environmentCancellation?.Cancel();
        panel.Controls.Add(actions);
        panel.SizeChanged += (_, _) =>
        {
            foreach (var label in new[] { _environmentRuntime, _environmentSdk, _environmentMessage })
                label.MaximumSize = new Size(Math.Max(100, panel.ClientSize.Width - 12), 0);
        };
        Shown += async (_, _) => await CheckDevelopmentEnvironmentAsync();
        FormClosing += (_, args) =>
        {
            // Once a system installer is running, let its own UI finish or cancel.
            if (_environmentInstallerRunning) { args.Cancel = true; return; }
            _environmentLifetime.Cancel();
            _environmentCancellation?.Cancel();
        };
        FormClosed += async (_, _) =>
        {
            _environmentLifetime.Cancel();
            if (_environmentOperation is { } operation) await operation;
            _environmentService.Dispose();
            _environmentLifetime.Dispose();
        };
        return panel;
    }

    private Task<DotNetEnvironmentSnapshot?> CheckDevelopmentEnvironmentAsync()
    {
        if (_environmentOperation is { IsCompleted: false }) return _environmentOperation;
        if (IsDisposed || _environmentLifetime.IsCancellationRequested) return Task.FromResult<DotNetEnvironmentSnapshot?>(null);
        return _environmentOperation = RunEnvironmentOperationAsync(install: false);
    }

    private async Task<DotNetEnvironmentSnapshot?> InstallDevelopmentEnvironmentAsync(bool confirm = true)
    {
        if (_environmentOperation is { IsCompleted: false }) return await _environmentOperation;
        if (_updateCancellation is not null || _startingUpdateInstaller || _needsUpdateRecovery) return null;
        if (confirm && MessageBox.Show(this,
                T("Hub will check again and install only missing .NET 10 components from Microsoft. Existing compatible versions are kept. Continue?"),
                T("Development environment"), MessageBoxButtons.OKCancel, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2) != DialogResult.OK) return null;
        return await (_environmentOperation = RunEnvironmentOperationAsync(install: true));
    }

    private async Task<DotNetEnvironmentSnapshot?> RunEnvironmentOperationAsync(bool install)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_environmentLifetime.Token);
        _environmentCancellation = cancellation;
        _environmentBusy = true;
        _environmentInstallerRunning = false;
        _environmentLastProgress = null;
        _environmentProgress.Visible = true;
        _environmentProgress.Style = ProgressBarStyle.Marquee;
        _environmentMessage.ForeColor = SecondaryText;
        SetLocalizedText(_environmentMessage, "Checking installed .NET environments...");
        RefreshEnvironmentActions();
        try
        {
            if (install)
            {
                var result = await _environmentService.EnsureReadyAsync(
                    new Progress<DotNetEnvironmentProgress>(ReportEnvironmentProgress), cancellation.Token);
                if (IsDisposed || _environmentLifetime.IsCancellationRequested) return null;
                _environmentSnapshot = result.Snapshot;
                UpdateEnvironmentVersions();
                SetLocalizedText(_environmentMessage, result.IsReady
                    ? "Environment ready. Compatible installations are reused."
                    : result.RestartRequired ? "Restart Windows, then check the environment again."
                    : result.Cancelled ? "Environment installation cancelled. You can retry when ready."
                    : "Required .NET components are still missing. Check the installer result and retry.");
            }
            else
            {
                _environmentSnapshot = await _environmentService.ProbeAsync(cancellation.Token);
                if (IsDisposed || _environmentLifetime.IsCancellationRequested) return null;
                UpdateEnvironmentVersions();
                SetLocalizedText(_environmentMessage, _environmentSnapshot.IsReady
                    ? "Environment ready. Compatible installations are reused."
                    : "Install the missing components before opening a project. Hub updates remain available.");
            }
            return _environmentSnapshot;
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed && !_environmentLifetime.IsCancellationRequested)
                SetLocalizedText(_environmentMessage, "Environment operation cancelled.");
            return null;
        }
        catch (Exception exception)
        {
            Trace.WriteLine(exception);
            if (!IsDisposed && !_environmentLifetime.IsCancellationRequested)
            {
                _environmentMessage.ForeColor = ErrorText;
                SetLocalizedText(_environmentMessage, "Environment check or installation failed: {0}", exception.Message);
            }
            return null;
        }
        finally
        {
            _environmentBusy = false;
            _environmentInstallerRunning = false;
            _environmentCancellation = null;
            if (!IsDisposed && !_environmentLifetime.IsCancellationRequested)
            {
                _environmentProgress.Visible = false;
                RefreshEnvironmentActions();
            }
        }
    }

    private void UpdateEnvironmentVersions()
    {
        if (_environmentSnapshot is not { } snapshot) return;
        SetLocalizedText(_environmentRuntime, "Desktop Runtime: {0}", snapshot.DesktopRuntimeVersion ?? T("Not installed"));
        SetLocalizedText(_environmentSdk, ".NET SDK: {0}", snapshot.SdkVersion ?? T("Not installed"));
    }

    private void RefreshEnvironmentActions()
    {
        var updating = _updateCancellation is not null || _startingUpdateInstaller || _needsUpdateRecovery;
        _environmentCheck.Enabled = !_environmentBusy && !updating;
        _environmentInstall.Enabled = !_environmentBusy && !updating && _environmentSnapshot?.IsReady != true;
        _environmentCancel.Visible = _environmentBusy;
        _environmentCancel.Enabled = !_environmentInstallerRunning;
        _checkUpdateButton.Enabled = !_environmentBusy && !updating;
        _installUpdateButton.Enabled = !_environmentBusy && !updating && _pendingUpdate?.HasUpdates == true;
    }

    private void ReportEnvironmentProgress(DotNetEnvironmentProgress progress)
    {
        if (IsDisposed || !_environmentBusy || _environmentLifetime.IsCancellationRequested) return;
        _environmentLastProgress = progress;
        _environmentInstallerRunning = progress.Stage == "Installing";
        _environmentCancel.Enabled = !_environmentInstallerRunning;
        if (progress.Percent is { } percent)
        {
            _environmentProgress.Style = ProgressBarStyle.Continuous;
            _environmentProgress.Value = (int)Math.Clamp(percent * 10, 0, 1000);
        }
        else _environmentProgress.Style = ProgressBarStyle.Marquee;
        var message = progress.Stage switch
        {
            "Checking" => "Checking installed .NET environments...",
            "Metadata" => "Reading Microsoft's .NET release information...",
            "Downloading" => "Downloading missing .NET components...",
            "Verifying" => "Verifying the Microsoft installer...",
            "Installing" => "Complete or cancel the installation in the Microsoft installer window.",
            "Ready" => "Environment ready. Compatible installations are reused.",
            _ => progress.Message
        };
        var amount = progress.TotalBytes is > 0
            ? $" · {FormatUpdateBytes(progress.BytesReceived)} / {FormatUpdateBytes(progress.TotalBytes)}"
            : string.Empty;
        SetLocalizedText(_environmentMessage, "{0}", T(message) + amount);
    }

    private async Task<DotNetEnvironmentSnapshot?> EnsureEditorEnvironmentAsync()
    {
        var snapshot = await CheckDevelopmentEnvironmentAsync();
        if (snapshot?.IsReady == true) return snapshot;
        if (IsDisposed || _environmentLifetime.IsCancellationRequested) return null;
        ShowSettingsPage();
        if (snapshot is null) return null;
        if (MessageBox.Show(this,
                T("Opening a project requires .NET 10 Desktop Runtime and SDK. Install the missing components now? Existing compatible versions are reused."),
                T("Development environment"), MessageBoxButtons.OKCancel, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2) != DialogResult.OK) return null;
        var installed = await InstallDevelopmentEnvironmentAsync(confirm: false);
        return installed?.IsReady == true ? installed : null;
    }

    private static void ConfigureEditorEnvironment(ProcessStartInfo start, DotNetEnvironmentSnapshot environment)
    {
        BEngine.Tooling.ToolEnvironment.ApplyTo(start.Environment);
        if (environment.RuntimeRoot is { Length: > 0 } runtimeRoot)
        {
            start.Environment["DOTNET_ROOT_X64"] = runtimeRoot;
            start.Environment["DOTNET_ROOT"] = runtimeRoot;
        }
        if (environment.SdkRoot is { Length: > 0 } sdkRoot)
        {
            start.Environment.TryGetValue("PATH", out var existingPath);
            start.Environment["PATH"] = sdkRoot + Path.PathSeparator + existingPath;
        }
    }
}
