using System.Collections.Concurrent;
using BEngine.AssetBundles;
using BEngine.Startup;

namespace BEngine.Player;

internal sealed class PlayerAotStartupFlow : IAotStartupFlow, IDisposable
{
    internal const string AutoCheckEnvironmentVariable = "BENGINE_AOT_AUTO_CHECK";
    internal const string AutoConfirmUpdateEnvironmentVariable = "BENGINE_AOT_AUTO_CONFIRM_UPDATE";
    internal const string AutoDeclineUpdateEnvironmentVariable = "BENGINE_AOT_AUTO_DECLINE_UPDATE";
    internal const string AutoContinueEnvironmentVariable = "BENGINE_AOT_AUTO_CONTINUE";

    private readonly Func<CancellationToken, BValueTask<AssetBundleUpdatePlan>> _check;
    private readonly Func<AssetBundleUpdatePlan, IProgress<AssetBundleUpdateProgress>, CancellationToken, BValueTask>
        _apply;
    private readonly Func<CancellationToken, BValueTask<(bool IsValid, string Error)>> _validateGameContent;
    private readonly bool _failStartupWhenUpdateFails;
    private readonly ConcurrentQueue<AotStartupSnapshot> _pending = new();
    private readonly CancellationTokenSource _lifetime = new();
    private AotStartupSnapshot _current = WaitingForCheck();
    private AotStartupSnapshot _latest = WaitingForCheck();
    private AssetBundleUpdatePlan? _pendingPlan;
    private int _operationRunning;
    private int _contentValidated;
    private int _enterGameRequested;
    private int _started;
    private int _disposed;

    internal PlayerAotStartupFlow(
        Func<CancellationToken, BValueTask<AssetBundleUpdatePlan>> check,
        Func<AssetBundleUpdatePlan, IProgress<AssetBundleUpdateProgress>, CancellationToken, BValueTask> apply,
        Func<CancellationToken, BValueTask<(bool IsValid, string Error)>> validateGameContent,
        bool failStartupWhenUpdateFails = true)
    {
        _check = check ?? throw new ArgumentNullException(nameof(check));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _validateGameContent = validateGameContent ?? throw new ArgumentNullException(nameof(validateGameContent));
        _failStartupWhenUpdateFails = failStartupWhenUpdateFails;
    }

    public AotStartupSnapshot Current => Volatile.Read(ref _current);

    public event Action<AotStartupSnapshot>? Changed;

    internal bool EnterGameRequested => Volatile.Read(ref _enterGameRequested) != 0;

    internal Action<AotStartupSnapshot>? StateEnqueuedForTesting { get; set; }

    internal void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) == 0) Enqueue(WaitingForCheck());
    }

    internal void ReportTransitionFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Interlocked.Exchange(ref _contentValidated, 0);
        Interlocked.Exchange(ref _enterGameRequested, 0);
        var latest = Volatile.Read(ref _latest);
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        while (_pending.TryDequeue(out var state))
        {
            Volatile.Write(ref _current, state);
            Changed?.Invoke(state);
        }

        if (Volatile.Read(ref _started) == 0) return;
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var latest = Volatile.Read(ref _latest);
        if ((!latest.CanCheckForUpdates && !latest.CanRetry) ||
            Interlocked.CompareExchange(ref _operationRunning, 1, 0) != 0) return;

        Interlocked.Exchange(ref _pendingPlan, null);
        Interlocked.Exchange(ref _contentValidated, 0);
        Interlocked.Exchange(ref _enterGameRequested, 0);
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!Volatile.Read(ref _latest).RequiresUpdateConfirmation ||
            Interlocked.CompareExchange(ref _operationRunning, 1, 0) != 0) return;
        var plan = Interlocked.Exchange(ref _pendingPlan, null);
        if (plan is null)
        {
            Interlocked.Exchange(ref _operationRunning, 0);
            return;
        }

        PlayerStartupDiagnostics.Phase("04_UPDATE_CONFIRMED",
            $"target={plan.TargetVersion.Version};bundles={plan.Downloads.Count};bytes={plan.DownloadSize}");
        Interlocked.Exchange(ref _contentValidated, 0);
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var latest = Volatile.Read(ref _latest);
        if (!latest.RequiresUpdateConfirmation || !latest.CanDeclineUpdate ||
            Volatile.Read(ref _contentValidated) == 0 ||
            Interlocked.CompareExchange(ref _operationRunning, 1, 0) != 0) return;
        var plan = Interlocked.Exchange(ref _pendingPlan, null);
        if (plan is null)
        {
            Interlocked.Exchange(ref _operationRunning, 0);
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Current.CanEnterGame && Volatile.Read(ref _contentValidated) != 0)
            Interlocked.Exchange(ref _enterGameRequested, 1);
    }

    private async BTask RunCheckAsync(CancellationToken cancellationToken)
    {
        AotStartupSnapshot? completed = null;
        try
        {
            var plan = await _check(cancellationToken).ConfigureAwait(false);
            if (plan.HasUpdates)
            {
                var installedContent = await ValidateInstalledContentForDeferralAsync(cancellationToken)
                    .ConfigureAwait(false);
                Interlocked.Exchange(ref _contentValidated, installedContent.IsValid ? 1 : 0);
                Interlocked.Exchange(ref _pendingPlan, plan);
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
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            completed = await TryCreateFallbackToLastValidAsync(
                    "AOT_UPDATE_CHECK", "Could not check for updates.", exception, cancellationToken)
                .ConfigureAwait(false) ??
                CreateFailureSnapshot("AOT_UPDATE_CHECK", "Could not check for updates.", exception);
        }
        finally { CompleteOperation(completed); }
    }

    private async BTask RunApplyAsync(
        AssetBundleUpdatePlan plan,
        CancellationToken cancellationToken)
    {
        AotStartupSnapshot? completed = null;
        try
        {
            var progress = new InlineProgress<AssetBundleUpdateProgress>(ReportProgress);
            await _apply(plan, progress, cancellationToken).ConfigureAwait(false);
            var latest = Volatile.Read(ref _latest);
            completed = await ValidateGameContentAsync(
                    "Version switch complete. Ready to start.",
                    "The version switch completed, but the game content is incomplete.",
                    plan.TargetVersion.Version,
                    plan.Downloads.Count,
                    latest.CompletedBytes,
                    Math.Max(latest.TotalBytes, plan.DownloadSize),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            completed = await TryCreateFallbackToLastValidAsync(
                    "AOT_UPDATE", "Update failed.", exception, cancellationToken)
                .ConfigureAwait(false) ??
                CreateFailureSnapshot("AOT_UPDATE", "Update failed.", exception);
        }
        finally { CompleteOperation(completed); }
    }

    private async BValueTask<(bool IsValid, string Error)> ValidateInstalledContentForDeferralAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _validateGameContent(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
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
        CancellationToken cancellationToken)
    {
        var validation = await _validateGameContent(cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            Interlocked.Exchange(ref _contentValidated, 0);
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

        Interlocked.Exchange(ref _contentValidated, 1);
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
        Interlocked.Exchange(ref _contentValidated, 0);
        Interlocked.Exchange(ref _pendingPlan, null);
        var latest = Volatile.Read(ref _latest);
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
        CancellationToken cancellationToken)
    {
        if (_failStartupWhenUpdateFails) return null;

        (bool IsValid, string Error) validation;
        try
        {
            validation = await _validateGameContent(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
        Interlocked.Exchange(ref _contentValidated, 1);
        Interlocked.Exchange(ref _pendingPlan, null);
        var latest = Volatile.Read(ref _latest);
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
        var latest = Volatile.Read(ref _latest);
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
        if (Volatile.Read(ref _disposed) != 0) return;
        Volatile.Write(ref _latest, state);
        _pending.Enqueue(state);
        StateEnqueuedForTesting?.Invoke(state);
    }

    private void CompleteOperation(AotStartupSnapshot? completed)
    {
        Interlocked.Exchange(ref _operationRunning, 0);
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

    private static bool IsAutomationEnabled(string name) =>
        Environment.GetEnvironmentVariable(name) == "1";

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _lifetime.Dispose();
        Interlocked.Exchange(ref _pendingPlan, null);
        while (_pending.TryDequeue(out _)) { }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
