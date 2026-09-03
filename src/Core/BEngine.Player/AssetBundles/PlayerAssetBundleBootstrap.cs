using BEngine.AssetBundles;
using BEngine.Content;

namespace BEngine.Player;

internal sealed class PlayerAssetBundleBootstrap : IContentBootstrapper, IDisposable
{
    private readonly IAssetBundleManager _manager;
    private readonly AssetBundleResourceProvider _provider;
    private readonly AssetBundleSettingsDocument _settings;
    private readonly bool _registerResourceProviderOnPrepare;
    private readonly object _resourceProviderGate = new();
    private readonly SemaphoreSlim _prepareGate = new(1, 1);
    private ActivatedContentRelease? _release;
    private int _initialized;
    private int _updateCompleted;
    private int _disposed;
    private int _resourceProviderRegistered;
    private int _rollbackAttempted;

    internal PlayerAssetBundleBootstrap(
        IAssetBundleManager manager,
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
        CancellationToken cancellationToken = default)
    {
        await PrepareBuiltInAsync(cancellationToken).ConfigureAwait(false);
        await ApplyStartupUpdateAsync(progress: null, cancellationToken).ConfigureAwait(false);
        return Volatile.Read(ref _release) ?? throw new InvalidOperationException(
            "The Player content release was not prepared.");
    }

    internal async BValueTask<ActivatedContentRelease> PrepareBuiltInAsync(
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _initialized) != 0 && Volatile.Read(ref _release) is { } active)
            return active;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _prepareGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _initialized) != 0 && Volatile.Read(ref _release) is { } prepared)
                return prepared;
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            await _manager.InitializeAsync(cancellationToken).ConfigureAwait(false);
            PlayerStartupDiagnostics.Phase(
                _registerResourceProviderOnPrepare
                    ? "03_BUILTIN_CONTENT_READY"
                    : "04_SANDBOX_CONTENT_READY",
                $"package={_settings.PackageName};version={_manager.ActiveVersion?.Version ?? "none"};" +
                $"cache={(_manager as AssetBundleManager)?.CacheDirectory ?? "virtual"}");
            if (_registerResourceProviderOnPrepare) ActivateResourceProvider();
            active = CreateRelease(updated: false);
            Volatile.Write(ref _release, active);
            Volatile.Write(ref _initialized, 1);
            return active;
        }
        finally { _prepareGate.Release(); }
    }

    internal async BValueTask ApplyStartupUpdateAsync(
        IProgress<AssetBundleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool allowFallback = true)
    {
        await PrepareBuiltInAsync(cancellationToken).ConfigureAwait(false);
        if (Volatile.Read(ref _updateCompleted) != 0) return;
        try
        {
            if (_settings.CheckForUpdatesOnStartup && !string.IsNullOrWhiteSpace(_settings.RemoteBaseUrl))
            {
                var plan = await CheckForStartupUpdateAsync(cancellationToken).ConfigureAwait(false);
                if (plan.HasUpdates && _settings.ApplyUpdatesOnStartup)
                    await ApplyCheckedUpdateAsync(plan, progress, cancellationToken).ConfigureAwait(false);
                else
                {
                    if (plan.HasUpdates)
                        Debug.Log(
                            $"AssetBundle '{_settings.PackageName}' update {plan.TargetVersion.Version} is available.");
                    await CompleteWithoutUpdateAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                PlayerStartupDiagnostics.Phase("04_UPDATE_SKIPPED",
                    string.IsNullOrWhiteSpace(_settings.RemoteBaseUrl) ? "reason=no-remote" : "reason=disabled");
                await CompleteWithoutUpdateAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (allowFallback && !_settings.FailStartupWhenUpdateFails &&
                                          exception is not OperationCanceledException)
        {
            PlayerStartupDiagnostics.Failure("UPDATE_FALLBACK", exception);
            Debug.LogWarning(
                $"AssetBundle update failed; continuing with the last valid version: {exception.Message}");
            await CompleteWithoutUpdateAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal async BValueTask<AssetBundleUpdatePlan> CheckForStartupUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        await PrepareBuiltInAsync(cancellationToken).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (string.IsNullOrWhiteSpace(_settings.RemoteBaseUrl))
            throw new InvalidOperationException(
                $"AssetBundle '{_settings.PackageName}' has no remote content URL configured.");

        PlayerStartupDiagnostics.Phase("04_UPDATE_CHECK_STARTED",
            $"package={_settings.PackageName}");
        var plan = await _manager.CheckForUpdatesAsync(cancellationToken).ConfigureAwait(false);
        PlayerStartupDiagnostics.Phase("04_UPDATE_PLAN_READY",
            $"target={plan.TargetVersion.Version};hasUpdates={plan.HasUpdates};" +
            $"bundles={plan.Downloads.Count};bytes={plan.DownloadSize}");
        return plan;
    }

    internal async BValueTask ApplyCheckedUpdateAsync(
        AssetBundleUpdatePlan plan,
        IProgress<AssetBundleUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await PrepareBuiltInAsync(cancellationToken).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _prepareGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _manager.ApplyUpdateAsync(plan, progress, cancellationToken)
                .ConfigureAwait(false);
            Volatile.Write(ref _release, CreateRelease(result.Updated));
            Volatile.Write(ref _updateCompleted, 1);
            PlayerStartupDiagnostics.Phase("04_CONTENT_ACTIVATED",
                $"previous={result.PreviousVersion};active={result.ActiveVersion};" +
                $"downloadedBundles={result.DownloadedBundleCount};downloadedBytes={result.DownloadedBytes}");
            Debug.Log($"AssetBundle '{_settings.PackageName}' activated version {result.ActiveVersion}.");
        }
        finally { _prepareGate.Release(); }
    }

    internal async BValueTask CompleteWithoutUpdateAsync(CancellationToken cancellationToken = default)
    {
        await PrepareBuiltInAsync(cancellationToken).ConfigureAwait(false);
        await _prepareGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _updateCompleted) != 0) return;
            Volatile.Write(ref _release, CreateRelease(updated: false));
            Volatile.Write(ref _updateCompleted, 1);
        }
        finally { _prepareGate.Release(); }
    }

    internal BValueTask<ActivatedContentRelease> InitializeAsync(
        CancellationToken cancellationToken = default) => PrepareAsync(cancellationToken);

    internal bool UpdatedThisStartup => Volatile.Read(ref _release)?.Updated == true;

    internal void ActivateResourceProvider()
    {
        lock (_resourceProviderGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_resourceProviderRegistered != 0) return;
            Resources.RegisterResourceProvider(_provider);
            _resourceProviderRegistered = 1;
        }
    }

    internal void DeactivateResourceProvider()
    {
        lock (_resourceProviderGate)
        {
            if (_resourceProviderRegistered == 0) return;
            Resources.UnregisterResourceProvider(_provider);
            _resourceProviderRegistered = 0;
        }
    }

    internal void CommitStartup()
    {
        if (_manager is not AssetBundleManager runtime || !runtime.HasPendingActivation) return;
        runtime.CommitPendingActivationAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        PlayerStartupDiagnostics.Phase("04_CONTENT_COMMITTED",
            $"active={_manager.ActiveVersion?.Version ?? "none"}");
    }

    internal void RollbackAfterStartupFailure(Exception startupFailure)
    {
        ArgumentNullException.ThrowIfNull(startupFailure);
        var hasPending = _manager is AssetBundleManager runtime && runtime.HasPendingActivation;
        if ((!UpdatedThisStartup && !hasPending) ||
            Interlocked.Exchange(ref _rollbackAttempted, 1) != 0) return;
        try
        {
            var rolledBack = _manager.RollbackAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            PlayerStartupDiagnostics.Phase("04_CONTENT_ROLLBACK",
                $"success={rolledBack};failedVersion={_release?.ReleaseId};reason={startupFailure.GetType().Name}");
            if (!rolledBack)
                Debug.LogError("The failed startup content release could not be rolled back.");
        }
        catch (Exception rollbackFailure)
        {
            PlayerStartupDiagnostics.Failure("CONTENT_ROLLBACK", rollbackFailure);
            Debug.LogError($"AssetBundle rollback failed after startup error: {rollbackFailure}");
        }
    }

    private ActivatedContentRelease CreateRelease(bool updated) => new(
        _manager.ActiveVersion?.Version ?? $"asset-bundle:{_settings.PackageName}:empty",
        ContentEnvironmentKind.PublishedRemote,
        _manager,
        updated);

    public void Dispose()
    {
        lock (_resourceProviderGate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (_resourceProviderRegistered == 0) return;
            Resources.UnregisterResourceProvider(_provider);
            _resourceProviderRegistered = 0;
        }
    }
}
