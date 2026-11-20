using BEngine.Platform;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor.Build;

public sealed class PlayerBuildPipelineAdapter : IPlayerBuildPipeline
{
    private readonly object _providerGate = new();
    private readonly List<IPlayerBuildTargetProvider> _providers = [];
    private readonly IPlayerBuildContentProvider _contentProvider;
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private readonly PlatformUtility _platformService;
    private readonly PlatformPlayerBuildCapabilities _buildCapabilities;

    public PlayerBuildPipelineAdapter(
        ProcessUtility processRunner,
        ApplicationPathUtility applicationPaths,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        PlatformPlayerBuildCapabilities buildCapabilities,
        IPlayerBuildContentProvider contentProvider)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(applicationPaths);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(platformService);
        _contentProvider = contentProvider ??
                                    throw new ArgumentNullException(nameof(contentProvider));
        _fileSystem = fileSystem;
        _pathService = pathService;
        _platformService = platformService;
        _buildCapabilities = buildCapabilities ??
                             throw new ArgumentNullException(nameof(buildCapabilities));
        RegisterWorkflowHosts(new PlatformBuildServices(processRunner, applicationPaths, fileSystem,
            pathService, platformService, buildCapabilities));
        foreach (var provider in PlatformBuildProviderLoader.Load(new PlatformBuildServices(
                     processRunner, applicationPaths, fileSystem, pathService, platformService, buildCapabilities)))
            RegisterProvider(_contentProvider.Adapt(provider, new PlatformBuildServices(processRunner, applicationPaths, fileSystem, pathService, platformService, buildCapabilities)));
    }

    private static void RegisterWorkflowHosts(PlatformBuildServices services)
    {
        foreach (var target in BEngine.Platform.BuildTargetUtility.Current.All)
        {
            if (!PlayerWorkflowHostLocator.TryResolve(target, null, out var host)) continue;
            services.Capabilities.RegisterHostProject(target.TargetId, host);
            if (target.HasBuiltInPlayerHost || services.Capabilities.TryGetRenderer(target.TargetId, out _)) continue;
            var bridge = target.Platform == BEngine.Build.BuildTargetPlatform.IOS ? "BEngine.Player.iOSHost" : null;
            services.Capabilities.RegisterRenderer(new PlatformPlayerRendererCapability(
                "bengine." + target.Platform.ToString().ToLowerInvariant(), target.TargetId,
                target.GraphicsBackends, host, SupportsHybridAotInterpreter: true,
                InterpreterBridgeAssemblyName: bridge));
        }
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
            _contentProvider,
            _fileSystem,
            _pathService,
            _platformService,
            _buildCapabilities,
            progress,
            cancellationToken);

    public async Task<PlayerContentUpdateResult> BuildContentUpdateAsync(
        PlayerBuildRequest request,
        string outputDirectory,
        IProgress<PlayerBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var hostModules = PlayerBuildHostModules.Enter(_contentProvider.RuntimeHostAnchors);
        return await _contentProvider.BuildContentUpdateAsync(request, outputDirectory, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    public void SetLatestContentVersion(
        string packageDirectory,
        string version,
        CancellationToken cancellationToken = default) =>
        _contentProvider.SetLatestContentVersion(packageDirectory, version, cancellationToken);

    private IReadOnlyList<IPlayerBuildTargetProvider> SnapshotProviders()
    {
        lock (_providerGate) return _providers.ToArray();
    }
}
