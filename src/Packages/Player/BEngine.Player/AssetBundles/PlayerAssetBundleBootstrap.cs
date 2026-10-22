using BEngine.AssetBundles;
using BEngine.Content;

namespace BEngine.Player;

internal sealed class PlayerAssetBundleBootstrap : IContentBootstrapper, IDisposable
{
    private readonly ITransactionalAssetBundleManager _manager;
    private readonly AssetBundleResourceProvider _provider;
    private readonly AssetBundleSettingsDocument _settings;
    private readonly bool _registerResourceProviderOnPrepare;
    private BTask<ActivatedContentRelease>? _prepareOperation;
    private ActivatedContentRelease? _release;
    private bool _initialized;
    private bool _updateCompleted;
    private bool _disposed;
    private bool _resourceProviderRegistered;
    private bool _rollbackInProgress;

    internal PlayerAssetBundleBootstrap(
        ITransactionalAssetBundleManager manager,
        AssetBundleResourceProvider provider,
        AssetBundleSettingsDocument settings,
        bool registerResourceProviderOnPrepare = true)
    {
        _manager = manager;
        _provider = provider;
        _settings = settings;
        _registerResourceProviderOnPrepare = registerResourceProviderOnPrepare;
    }

    public async BValueTask<ActivatedContentRelease> PrepareAsync(
        BCancellationToken cancellationToken = default)
    {
        await PrepareBuiltInAsync(cancellationToken);
        await ApplyStartupUpdateAsync(progress: null, cancellationToken);
        return _release ?? throw new InvalidOperationException(
            "The Player content release was not prepared.");
    }

    internal BValueTask<ActivatedContentRelease> PrepareBuiltInAsync(
        BCancellationToken cancellationToken = default)
    {
        if (_initialized && _release is { } active)
            return BValueTask<ActivatedContentRelease>.FromResult(active);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _prepareOperation ??= PrepareBuiltInCoreAsync(cancellationToken);
        return new BValueTask<ActivatedContentRelease>(_prepareOperation);
    }

    private async BTask<ActivatedContentRelease> PrepareBuiltInCoreAsync(
        BCancellationToken cancellationToken)
    {
        await _manager.InitializeAsync(cancellationToken);
        ObjectDisposedException.ThrowIf(_disposed, this);
        PlayerStartupDiagnostics.Phase(
            _registerResourceProviderOnPrepare
                ? "03_BUILTIN_CONTENT_READY"
                : "04_SANDBOX_CONTENT_READY",
            $"package={_settings.PackageName};version={_manager.ActiveVersion?.Version ?? "none"}");
        if (_registerResourceProviderOnPrepare) ActivateResourceProvider();
        _release = CreateRelease(updated: false);
        _initialized = true;
        return _release;
    }

    internal async BValueTask ApplyStartupUpdateAsync(
        IProgress<AssetBundleUpdateProgress>? progress = null,
        BCancellationToken cancellationToken = default,
        bool allowFallback = true)
    {
        await PrepareBuiltInAsync(cancellationToken);
        if (_updateCompleted) return;
        try
        {
            if (_settings.CheckForUpdatesOnStartup && !string.IsNullOrWhiteSpace(_settings.RemoteBaseUrl))
            {
                var plan = await CheckForStartupUpdateAsync(cancellationToken);
                if (plan.HasUpdates && _settings.ApplyUpdatesOnStartup)
                    await ApplyCheckedUpdateAsync(plan, progress, cancellationToken);
                else
                {
                    if (plan.HasUpdates)
                        Debug.Log(
                            $"AssetBundle '{_settings.PackageName}' update {plan.TargetVersion.Version} is available.");
                    await CompleteWithoutUpdateAsync(cancellationToken);
                }
            }
            else
            {
                PlayerStartupDiagnostics.Phase("04_UPDATE_SKIPPED",
                    string.IsNullOrWhiteSpace(_settings.RemoteBaseUrl) ? "reason=no-remote" : "reason=disabled");
                await CompleteWithoutUpdateAsync(cancellationToken);
            }
        }
        catch (Exception exception) when (allowFallback && !_settings.FailStartupWhenUpdateFails &&
                                          exception is not BOperationCanceledException)
        {
            PlayerStartupDiagnostics.Failure("UPDATE_FALLBACK", exception);
            Debug.LogWarning(
                $"AssetBundle update failed; continuing with the last valid version: {exception.Message}");
            await CompleteWithoutUpdateAsync(cancellationToken);
        }
    }

    internal async BValueTask<AssetBundleUpdatePlan> CheckForStartupUpdateAsync(
        BCancellationToken cancellationToken = default)
    {
        await PrepareBuiltInAsync(cancellationToken);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(_settings.RemoteBaseUrl))
            throw new InvalidOperationException(
                $"AssetBundle '{_settings.PackageName}' has no remote content URL configured.");

        PlayerStartupDiagnostics.Phase("04_UPDATE_CHECK_STARTED",
            $"package={_settings.PackageName}");
        var plan = await _manager.CheckForUpdatesAsync(cancellationToken);
        PlayerStartupDiagnostics.Phase("04_UPDATE_PLAN_READY",
            $"target={plan.TargetVersion.Version};hasUpdates={plan.HasUpdates};" +
            $"bundles={plan.Downloads.Count};bytes={plan.DownloadSize}");
        return plan;
    }

    internal async BValueTask ApplyCheckedUpdateAsync(
        AssetBundleUpdatePlan plan,
        IProgress<AssetBundleUpdateProgress>? progress = null,
        BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await PrepareBuiltInAsync(cancellationToken);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_updateCompleted) return;
        var result = await _manager.ApplyUpdateAsync(plan, progress, cancellationToken);
        _release = CreateRelease(result.Updated);
        _updateCompleted = true;
        PlayerStartupDiagnostics.Phase("04_CONTENT_ACTIVATED",
            $"previous={result.PreviousVersion};active={result.ActiveVersion};" +
            $"downloadedBundles={result.DownloadedBundleCount};downloadedBytes={result.DownloadedBytes}");
        Debug.Log($"AssetBundle '{_settings.PackageName}' activated version {result.ActiveVersion}.");
    }

    internal async BValueTask CompleteWithoutUpdateAsync(BCancellationToken cancellationToken = default)
    {
        await PrepareBuiltInAsync(cancellationToken);
        if (_updateCompleted) return;
        _release = CreateRelease(updated: false);
        _updateCompleted = true;
    }

    internal BValueTask<ActivatedContentRelease> InitializeAsync(
        BCancellationToken cancellationToken = default) => PrepareAsync(cancellationToken);

    internal bool UpdatedThisStartup => _release?.Updated == true;

    internal void ActivateResourceProvider()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_resourceProviderRegistered) return;
        Resources.RegisterResourceProvider(_provider);
        _resourceProviderRegistered = true;
    }

    internal void DeactivateResourceProvider()
    {
        if (!_resourceProviderRegistered) return;
        Resources.UnregisterResourceProvider(_provider);
        _resourceProviderRegistered = false;
    }

    internal async BValueTask CommitStartupAsync(BCancellationToken cancellationToken = default)
    {
        if (!_manager.HasPendingActivation) return;
        await _manager.CommitPendingActivationAsync(cancellationToken);
        PlayerStartupDiagnostics.Phase("04_CONTENT_COMMITTED",
            $"active={_manager.ActiveVersion?.Version ?? "none"}");
    }

    internal async BValueTask RollbackAfterStartupFailureAsync(
        Exception startupFailure,
        BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startupFailure);
        if (!UpdatedThisStartup && !_manager.HasPendingActivation) return;
        if (_rollbackInProgress)
            throw new InvalidOperationException(
                "An AssetBundle startup rollback is already in progress.");
        _rollbackInProgress = true;
        try
        {
            var rolledBack = await _manager.RollbackAsync(cancellationToken);
            PlayerStartupDiagnostics.Phase("04_CONTENT_ROLLBACK",
                $"success={rolledBack};failedVersion={_release?.ReleaseId};reason={startupFailure.GetType().Name}");
            if (!rolledBack)
                throw new InvalidOperationException(
                    "The failed startup content release could not be rolled back.");
            _release = CreateRelease(updated: false);
            _updateCompleted = false;
        }
        catch (Exception rollbackFailure)
        {
            PlayerStartupDiagnostics.Failure("CONTENT_ROLLBACK", rollbackFailure);
            Debug.LogError($"AssetBundle rollback failed after startup error: {rollbackFailure}");
            throw new AggregateException(
                "AssetBundle rollback failed after the Player startup transaction failed.",
                startupFailure,
                rollbackFailure);
        }
        finally { _rollbackInProgress = false; }
    }

    private ActivatedContentRelease CreateRelease(bool updated) => new(
        _manager.ActiveVersion?.Version ?? $"asset-bundle:{_settings.PackageName}:empty",
        ContentEnvironmentKind.PublishedRemote,
        _manager,
        updated);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_resourceProviderRegistered) return;
        Resources.UnregisterResourceProvider(_provider);
        _resourceProviderRegistered = false;
    }
}
