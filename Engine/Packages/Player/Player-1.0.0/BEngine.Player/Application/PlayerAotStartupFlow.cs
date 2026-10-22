using BEngine.AssetBundles;
using BEngine.Platform;
using BEngine.Startup;

namespace BEngine.Player;

internal sealed class PlayerAotStartupFlow : IAotStartupFlow, IDisposable
{
    internal const string AutoCheckEnvironmentVariable = "BENGINE_AOT_AUTO_CHECK";
    internal const string AutoConfirmUpdateEnvironmentVariable = "BENGINE_AOT_AUTO_CONFIRM_UPDATE";
    internal const string AutoDeclineUpdateEnvironmentVariable = "BENGINE_AOT_AUTO_DECLINE_UPDATE";
    internal const string AutoContinueEnvironmentVariable = "BENGINE_AOT_AUTO_CONTINUE";

    private readonly Func<BCancellationToken, BValueTask<AssetBundleUpdatePlan>> _check;
    private readonly Func<AssetBundleUpdatePlan, IProgress<AssetBundleUpdateProgress>, BCancellationToken, BValueTask> _apply;
    private readonly Func<BCancellationToken, BValueTask<(bool IsValid, string Error)>> _validateGameContent;
    private readonly bool _failStartupWhenUpdateFails;
    private readonly PlatformUtility _platformService;
    private readonly Queue<AotStartupSnapshot> _pending = new();
    private readonly BCancellationSource _lifetime = new();
    private AotStartupSnapshot _current = WaitingForCheck();
    private AotStartupSnapshot _latest = WaitingForCheck();
    private AssetBundleUpdatePlan? _pendingPlan;
    private bool _operationRunning;
    private bool _contentValidated;
    private bool _enterGameRequested;
    private bool _started;
    private bool _disposed;

    internal PlayerAotStartupFlow(
        Func<BCancellationToken, BValueTask<AssetBundleUpdatePlan>> check,
        Func<AssetBundleUpdatePlan, IProgress<AssetBundleUpdateProgress>, BCancellationToken, BValueTask> apply,
        Func<BCancellationToken, BValueTask<(bool IsValid, string Error)>> validateGameContent,
        bool failStartupWhenUpdateFails = true,
        PlatformUtility? platformService = null)
    {
        _check = check ?? throw new ArgumentNullException(nameof(check));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _validateGameContent = validateGameContent ?? throw new ArgumentNullException(nameof(validateGameContent));
        _failStartupWhenUpdateFails = failStartupWhenUpdateFails;
        _platformService = platformService ?? RuntimeServiceAccess.PlatformService;
    }

    public AotStartupSnapshot Current => _current;

    public event Action<AotStartupSnapshot>? Changed;

    internal bool EnterGameRequested => _enterGameRequested;

    internal Action<AotStartupSnapshot>? StateEnqueuedForTesting { get; set; }

    internal void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        _started = true;
        Enqueue(WaitingForCheck());
    }

    internal void ReportTransitionFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _contentValidated = false;
        _enterGameRequested = false;
        var latest = _latest;
        Enqueue(new AotStartupSnapshot(
            AotStartupPhase.Failed,
            "Could not start the updated game.",
            latest.Progress,
            latest.CompletedBytes,
            latest.TotalBytes,
            false,
            false,
            false,
            true,
            latest.TargetVersion,
            latest.UpdateBundleCount,
            exception.Message));
    }

    internal void Pump()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (_pending.Count > 0)
        {
            var state = _pending.Dequeue();
            _current = state;
            Changed?.Invoke(state);
        }

        if (!_started) return;
        var current = Current;
        if (current.CanCheckForUpdates && IsAutomationEnabled(AutoCheckEnvironmentVariable))
            CheckForUpdates();
        else if (current.RequiresUpdateConfirmation)
        {
            if (current.CanDeclineUpdate && IsAutomationEnabled(AutoDeclineUpdateEnvironmentVariable))
                DeclineUpdate();
            else if (IsAutomationEnabled(AutoConfirmUpdateEnvironmentVariable))
                ConfirmUpdate();
        }
        else if (current.CanEnterGame && IsAutomationEnabled(AutoContinueEnvironmentVariable))
            EnterGame();
    }

    public void CheckForUpdates()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var latest = _latest;
        if ((!latest.CanCheckForUpdates && !latest.CanRetry) || _operationRunning) return;

        _operationRunning = true;
        _pendingPlan = null;
        _contentValidated = false;
        _enterGameRequested = false;
        Enqueue(new AotStartupSnapshot(
            AotStartupPhase.CheckingForUpdates,
            "Checking for updates...",
            0.03f,
            0,
            0,
            false,
            false,
            false,
            false));
        _ = RunCheckAsync(_lifetime.Token);
    }

    public void ConfirmUpdate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_latest.RequiresUpdateConfirmation || _operationRunning) return;
        _operationRunning = true;
        var plan = _pendingPlan;
        _pendingPlan = null;
        if (plan is null)
        {
            _operationRunning = false;
            return;
        }

        PlayerStartupDiagnostics.Phase("04_UPDATE_CONFIRMED",
            $"target={plan.TargetVersion.Version};bundles={plan.Downloads.Count};bytes={plan.DownloadSize}");
        _contentValidated = false;
        Enqueue(new AotStartupSnapshot(
            AotStartupPhase.Downloading,
            "Preparing remote version switch...",
            0.08f,
            0,
            plan.DownloadSize,
            false,
            false,
            false,
            false,
            plan.TargetVersion.Version,
            plan.Downloads.Count));
        _ = RunApplyAsync(plan, _lifetime.Token);
    }

    public void DeclineUpdate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var latest = _latest;
        if (!latest.RequiresUpdateConfirmation || !latest.CanDeclineUpdate ||
            !_contentValidated || _operationRunning) return;
        _operationRunning = true;
        var plan = _pendingPlan;
        _pendingPlan = null;
        if (plan is null)
        {
            _operationRunning = false;
            return;
        }
        PlayerStartupDiagnostics.Phase("04_UPDATE_DECLINED",
            $"target={plan.TargetVersion.Version};bundles={plan.Downloads.Count};bytes={plan.DownloadSize}");
        CompleteOperation(new AotStartupSnapshot(
            AotStartupPhase.Ready,
            $"Switch to {plan.TargetVersion.Version} postponed. Installed content is ready.",
            1,
            0,
            plan.DownloadSize,
            false,
            false,
            true,
            false,
            plan.TargetVersion.Version,
            plan.Downloads.Count));
    }

    public void Retry() => CheckForUpdates();

    public void EnterGame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Current.CanEnterGame && _contentValidated) _enterGameRequested = true;
    }

    private async BValueTask RunCheckAsync(BCancellationToken cancellationToken)
    {
        AotStartupSnapshot? completed = null;
        try
        {
            var plan = await _check(cancellationToken);
            if (plan.HasUpdates)
            {
                var installedContent = await ValidateInstalledContentForDeferralAsync(cancellationToken);
                _contentValidated = installedContent.IsValid;
                _pendingPlan = plan;
                PlayerStartupDiagnostics.Phase("04_UPDATE_CONFIRMATION_REQUIRED",
                    $"target={plan.TargetVersion.Version};bundles={plan.Downloads.Count};" +
                    $"bytes={plan.DownloadSize};canDecline={installedContent.IsValid}");
                completed = new AotStartupSnapshot(
                    AotStartupPhase.AwaitingUpdateConfirmation,
                    installedContent.IsValid
                        ? $"Remote version {plan.TargetVersion.Version} is available."
                        : $"Remote version {plan.TargetVersion.Version} is required.",
                    0.06f,
                    0,
                    plan.DownloadSize,
                    false,
                    true,
                    false,
                    false,
                    plan.TargetVersion.Version,
                    plan.Downloads.Count,
                    CanDeclineUpdate: installedContent.IsValid);
                return;
            }

            completed = await ValidateGameContentAsync(
                    "Installed content is up to date.",
                    "Game content is not installed. Check for updates again.",
                    plan.TargetVersion.Version,
                    plan.Downloads.Count,
                    0,
                    plan.DownloadSize,
                    cancellationToken);
        }
        catch (BOperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            completed = await TryCreateFallbackToLastValidAsync(
                    "AOT_UPDATE_CHECK", "Could not check for updates.", exception, cancellationToken) ??
                CreateFailureSnapshot("AOT_UPDATE_CHECK", "Could not check for updates.", exception);
        }
        finally { CompleteOperation(completed); }
    }

    private async BValueTask RunApplyAsync(
        AssetBundleUpdatePlan plan,
        BCancellationToken cancellationToken)
    {
        AotStartupSnapshot? completed = null;
        try
        {
            var progress = new InlineProgress<AssetBundleUpdateProgress>(ReportProgress);
            await _apply(plan, progress, cancellationToken);
            var latest = _latest;
            completed = await ValidateGameContentAsync(
                    "Version switch complete. Ready to start.",
                    "The version switch completed, but the game content is incomplete.",
                    plan.TargetVersion.Version,
                    plan.Downloads.Count,
                    latest.CompletedBytes,
                    Math.Max(latest.TotalBytes, plan.DownloadSize),
                    cancellationToken);
        }
        catch (BOperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            completed = await TryCreateFallbackToLastValidAsync(
                    "AOT_UPDATE", "Update failed.", exception, cancellationToken) ??
                CreateFailureSnapshot("AOT_UPDATE", "Update failed.", exception);
        }
        finally { CompleteOperation(completed); }
    }

    private async BValueTask<(bool IsValid, string Error)> ValidateInstalledContentForDeferralAsync(
        BCancellationToken cancellationToken)
    {
        try
        {
            return await _validateGameContent(cancellationToken);
        }
        catch (BOperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            PlayerStartupDiagnostics.Failure("AOT_INSTALLED_CONTENT_VALIDATION", exception);
            Debug.LogWarning(
                $"The installed game release cannot be used to postpone the update: {exception.Message}");
            return (false, exception.Message);
        }
    }

    private async BValueTask<AotStartupSnapshot> ValidateGameContentAsync(
        string readyStatus,
        string missingStatus,
        string targetVersion,
        int updateBundleCount,
        long completedBytes,
        long totalBytes,
        BCancellationToken cancellationToken)
    {
        var validation = await _validateGameContent(cancellationToken);
        if (!validation.IsValid)
        {
            _contentValidated = false;
            return new AotStartupSnapshot(
                AotStartupPhase.GameContentMissing,
                missingStatus,
                0,
                completedBytes,
                totalBytes,
                false,
                false,
                false,
                true,
                targetVersion,
                updateBundleCount,
                string.IsNullOrWhiteSpace(validation.Error)
                    ? "The sandbox has no valid HotUpdate game release."
                    : validation.Error);
        }

        _contentValidated = true;
        return new AotStartupSnapshot(
            AotStartupPhase.Ready,
            readyStatus,
            1,
            completedBytes,
            totalBytes,
            false,
            false,
            true,
            false,
            targetVersion,
            updateBundleCount);
    }

    private AotStartupSnapshot CreateFailureSnapshot(string phase, string status, Exception exception)
    {
        PlayerStartupDiagnostics.Failure(phase, exception);
        _contentValidated = false;
        _pendingPlan = null;
        var latest = _latest;
        return new AotStartupSnapshot(
            AotStartupPhase.Failed,
            status,
            latest.Progress,
            latest.CompletedBytes,
            latest.TotalBytes,
            false,
            false,
            false,
            true,
            latest.TargetVersion,
            latest.UpdateBundleCount,
            exception.Message);
    }

    private async BValueTask<AotStartupSnapshot?> TryCreateFallbackToLastValidAsync(
        string phase,
        string status,
        Exception updateFailure,
        BCancellationToken cancellationToken)
    {
        if (_failStartupWhenUpdateFails) return null;

        (bool IsValid, string Error) validation;
        try
        {
            validation = await _validateGameContent(cancellationToken);
        }
        catch (BOperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception validationFailure)
        {
            Debug.LogWarning(
                $"Could not validate the last installed game release after an update failure: " +
                validationFailure.Message);
            return null;
        }
        if (!validation.IsValid) return null;

        PlayerStartupDiagnostics.Failure("UPDATE_FALLBACK", updateFailure);
        Debug.LogWarning(
            $"{phase}: {status} Continuing with the last valid sandbox release: {updateFailure.Message}");
        _contentValidated = true;
        _pendingPlan = null;
        var latest = _latest;
        return new AotStartupSnapshot(
            AotStartupPhase.Ready,
            $"{status} Using installed content.",
            1,
            latest.CompletedBytes,
            latest.TotalBytes,
            false,
            false,
            true,
            true,
            latest.TargetVersion,
            latest.UpdateBundleCount,
            updateFailure.Message);
    }

    private void ReportProgress(AssetBundleUpdateProgress progress)
    {
        var phase = progress.Phase switch
        {
            AssetBundleUpdatePhase.Downloading => AotStartupPhase.Downloading,
            AssetBundleUpdatePhase.Verifying => AotStartupPhase.Verifying,
            AssetBundleUpdatePhase.Activating => AotStartupPhase.Activating,
            AssetBundleUpdatePhase.Completed => AotStartupPhase.Activating,
            _ => AotStartupPhase.Downloading
        };
        var ratio = progress.TotalBytes > 0
            ? Math.Clamp((float)progress.CompletedBytes / progress.TotalBytes, 0, 1)
            : progress.TotalBundles > 0
                ? Math.Clamp((float)progress.CompletedBundles / progress.TotalBundles, 0, 1)
                : 0;
        var normalized = phase switch
        {
            AotStartupPhase.Downloading => 0.08f + ratio * 0.82f,
            AotStartupPhase.Verifying => 0.93f,
            AotStartupPhase.Activating => 0.97f,
            _ => ratio
        };
        var status = phase switch
        {
            AotStartupPhase.Downloading when !string.IsNullOrWhiteSpace(progress.BundleName) =>
                $"Downloading {progress.BundleName}...",
            AotStartupPhase.Downloading => "Downloading update...",
            AotStartupPhase.Verifying => "Verifying update...",
            AotStartupPhase.Activating => "Activating update...",
            _ => "Updating..."
        };
        var latest = _latest;
        Enqueue(new AotStartupSnapshot(
            phase,
            status,
            normalized,
            progress.CompletedBytes,
            progress.TotalBytes,
            false,
            false,
            false,
            false,
            latest.TargetVersion,
            latest.UpdateBundleCount));
    }

    private void Enqueue(AotStartupSnapshot state)
    {
        if (_disposed) return;
        _latest = state;
        _pending.Enqueue(state);
        StateEnqueuedForTesting?.Invoke(state);
    }

    private void CompleteOperation(AotStartupSnapshot? completed)
    {
        _operationRunning = false;
        if (completed is not null) Enqueue(completed);
    }

    private static AotStartupSnapshot WaitingForCheck() => new(
        AotStartupPhase.WaitingForUpdateCheck,
        "Check for updates to continue.",
        0,
        0,
        0,
        true,
        false,
        false,
        false);

    private bool IsAutomationEnabled(string name) =>
        _platformService.GetEnvironmentVariable(name) == "1";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _pendingPlan = null;
        _pending.Clear();
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
