using BEngine.Platform;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor;

public sealed class PlayerBuildPipelineAdapter : IPlayerBuildPipeline
{
    private readonly object _providerGate = new();
    private readonly List<IPlayerBuildTargetProvider> _providers = [];
    private readonly IAssetBundleBuildPipeline _assetBundleBuildPipeline;
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private readonly PlatformUtility _platformService;
    private readonly PlatformPlayerBuildCapabilities _buildCapabilities;
    private readonly IProjectAssetDatabaseFactory _assetDatabaseFactory;

    public PlayerBuildPipelineAdapter(
        ProcessUtility processRunner,
        ApplicationPathUtility applicationPaths,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        PlatformPlayerBuildCapabilities buildCapabilities,
        IAssetBundleBuildPipeline assetBundleBuildPipeline,
        IProjectAssetDatabaseFactory assetDatabaseFactory)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(applicationPaths);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(platformService);
        _assetBundleBuildPipeline = assetBundleBuildPipeline ??
                                    throw new ArgumentNullException(nameof(assetBundleBuildPipeline));
        _fileSystem = fileSystem;
        _pathService = pathService;
        _platformService = platformService;
        _buildCapabilities = buildCapabilities ??
                             throw new ArgumentNullException(nameof(buildCapabilities));
        _assetDatabaseFactory = assetDatabaseFactory ??
                                throw new ArgumentNullException(nameof(assetDatabaseFactory));
        foreach (var provider in PlatformBuildProviderLoader.Load(new PlatformBuildServices(
                     processRunner, applicationPaths, fileSystem, pathService, platformService, buildCapabilities)))
            RegisterProvider(provider);
    }

    public IReadOnlyList<IPlayerBuildTargetProvider> RegisteredProviders => SnapshotProviders();

    public void RegisterProvider(IPlayerBuildTargetProvider provider, bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (_providerGate)
        {
            var existing = _providers.FindIndex(item =>
                item.ProviderId.Equals(provider.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0 && !replace)
                throw new InvalidOperationException(
                    $"Build provider '{provider.ProviderId}' is already registered.");
            if (existing >= 0) _providers[existing] = provider;
            else _providers.Add(provider);
        }
    }

    public bool UnregisterProvider(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        lock (_providerGate)
            return _providers.RemoveAll(item =>
                item.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase)) != 0;
    }

    public bool CanBuild(string targetId, out string reason) =>
        PlayerBuildPipeline.CanBuildCore(targetId, SnapshotProviders(), out reason);

    public IReadOnlyList<PlayerBuildPrerequisite> GetPrerequisites(string targetId) =>
        PlayerBuildPipeline.GetPrerequisitesCore(targetId, SnapshotProviders());

    public Task<PlayerBuildResult> BuildAsync(
        PlayerBuildRequest request,
        IProgress<PlayerBuildProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        PlayerBuildPipeline.BuildCoreAsync(
            request,
            SnapshotProviders(),
            _assetBundleBuildPipeline,
            _assetDatabaseFactory,
            _fileSystem,
            _pathService,
            _platformService,
            _buildCapabilities,
            progress,
            cancellationToken);

    public Task<PlayerContentUpdateResult> BuildContentUpdateAsync(
        PlayerBuildRequest request,
        string outputDirectory,
        IProgress<PlayerBuildProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        PlayerContentUpdatePipeline.BuildCoreAsync(
            request,
            outputDirectory,
            _assetBundleBuildPipeline,
            _assetDatabaseFactory,
            _fileSystem,
            _pathService,
            _platformService,
            _buildCapabilities,
            progress,
            cancellationToken);

    public void SetLatestContentVersion(
        string packageDirectory,
        string version,
        CancellationToken cancellationToken = default) =>
        PlayerContentUpdatePipeline.SetLatestVersionCore(
            packageDirectory, version, _fileSystem, _pathService, _platformService,
            cancellationToken);

    private IReadOnlyList<IPlayerBuildTargetProvider> SnapshotProviders()
    {
        lock (_providerGate) return _providers.ToArray();
    }
}
