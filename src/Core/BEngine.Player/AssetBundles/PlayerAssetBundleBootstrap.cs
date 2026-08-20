using BEngine.AssetBundles;

namespace BEngine.Player;

internal sealed class PlayerAssetBundleBootstrap : IDisposable
{
    private readonly IAssetBundleManager _manager;
    private readonly AssetBundleResourceProvider _provider;
    private readonly AssetBundleSettingsDocument _settings;
    private int _initialized;
    private int _disposed;

    internal PlayerAssetBundleBootstrap(
        IAssetBundleManager manager,
        AssetBundleResourceProvider provider,
        AssetBundleSettingsDocument settings)
    {
        _manager = manager;
        _provider = provider;
        _settings = settings;
    }

    internal async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _initialized) != 0) return;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _manager.InitializeAsync(cancellationToken).ConfigureAwait(false);

        if (_settings.CheckForUpdatesOnStartup && !string.IsNullOrWhiteSpace(_settings.RemoteBaseUrl))
        {
            try
            {
                var plan = await _manager.CheckForUpdatesAsync(cancellationToken).ConfigureAwait(false);
                if (plan.HasUpdates && _settings.ApplyUpdatesOnStartup)
                {
                    var result = await _manager.ApplyUpdateAsync(plan, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    Debug.Log($"AssetBundle '{_settings.PackageName}' activated version {result.ActiveVersion}.");
                }
                else if (plan.HasUpdates)
                {
                    Debug.Log($"AssetBundle '{_settings.PackageName}' update {plan.TargetVersion.Version} is available.");
                }
            }
            catch (Exception exception) when (!_settings.FailStartupWhenUpdateFails &&
                                              exception is not OperationCanceledException)
            {
                Debug.LogWarning(
                    $"AssetBundle update failed; continuing with the last valid version: {exception.Message}");
            }
        }

        Resources.RegisterResourceProvider(_provider);
        Volatile.Write(ref _initialized, 1);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (Volatile.Read(ref _initialized) != 0) Resources.UnregisterResourceProvider(_provider);
    }
}
