using BEngine.AssetBundles;
using BEngine.Platform;
using BEngine.ProjectSystem.Editor;
using BEngine.Player.ProjectSystem;
using BEngine.Content;
using BEngine.Hosting;
namespace BEngine.Editor.Build;
internal sealed class AssetBundlePlayerBuildContentProvider(
    IAssetBundleBuildPipeline assetBundleBuildPipeline,
    IProjectAssetDatabaseFactory assetDatabaseFactory,
    FileUtility fileSystem, PathUtility pathService, PlatformUtility platformService,
    PlatformPlayerBuildCapabilities buildCapabilities) : IPlayerBuildContentProvider
{
    private readonly IAssetBundleBuildPipeline _assetBundleBuildPipeline = assetBundleBuildPipeline;
    private readonly IProjectAssetDatabaseFactory _assetDatabaseFactory = assetDatabaseFactory;
    private readonly PlatformPlayerBuildCapabilities _buildCapabilities = buildCapabilities;
    private static readonly Type[] HostAnchors = [typeof(AssetBundleManager)];
    private static readonly ResourcePackagingRequirements ResourcePackaging =
        ResourcePackagingRequirements.Create([".bassetbundle"]);
    public IReadOnlyList<Type> RuntimeHostAnchors => HostAnchors;
    public ResourcePackagingRequirements PackagingRequirements => ResourcePackaging;

    public HostDeploymentManifest? ReadDeploymentManifest(string playerDirectory, string productName)
    {
        var playerName = PlayerBuildLayout.SanitizePlayerName(productName);
        var dataDirectory = pathService.Combine(playerDirectory,
            $"{playerName}{HostPackagedResourceAddresses.DataDirectorySuffix}");
        var runtimeRoot = fileSystem.FileExists(pathService.Combine(dataDirectory,
            HostPackagedResourceAddresses.ResourcesDirectoryName, HostPackagedResourceAddresses.PlayerArchiveFileName))
            ? dataDirectory : pathService.Combine(dataDirectory, "res");
        return fileSystem.DirectoryExists(runtimeRoot)
            ? PlayerWorkspace.OpenRuntime(runtimeRoot, fileSystem, pathService).GetRuntimeMetadata()?.PlayerBootstrap
            : null;
    }

    public async Task PrepareAsync(PlayerBuildContext context, CancellationToken cancellationToken)
    {
        context.ContentEncryptionKey = context.Request.EncryptContent
            ? ContentEncryptionKey.FromPassphrase(context.Request.EncryptionKey) : null;
        context.ContentVersion = await PlayerContentBuildPipeline.BuildAsync(
                    context, _assetBundleBuildPipeline, _assetDatabaseFactory, fileSystem, pathService,
                    _buildCapabilities, cancellationToken)
                .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public Task FinalizeAsync(PlayerBuildContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var workspace = BEngine.ProjectSystem.ProjectWorkspace.Open(context.Request.ProjectPath, fileSystem, pathService);
        var layout = PlayerBuildLayout.Create(workspace, context.StagingDirectory, fileSystem, pathService);
        var metadata = RuntimeMetadataSerializer.Load(layout.RuntimeMetadataStagingPath, fileSystem, pathService);
        metadata.PlayerBootstrap = context.DeploymentManifest ??
            throw new InvalidOperationException("The engine has not prepared the Player deployment manifest.");
        RuntimeMetadataSerializer.Save(metadata, layout.RuntimeMetadataStagingPath, fileSystem, pathService);
        PlayerPackagedResourceArchiveBuilder.Write(layout, context.Request.GraphicsBackends,
            fileSystem, pathService, platformService, cancellationToken, context);
        if (fileSystem.FileExists(layout.RuntimeMetadataStagingPath))
            fileSystem.DeleteFile(layout.RuntimeMetadataStagingPath);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public IPlayerBuildTargetProvider Adapt(IPlatformBuildTargetProvider provider, PlatformBuildServices services) =>
        new AssetBundlePlatformBuildTargetAdapter(provider, fileSystem, pathService);
    public Task<PlayerContentUpdateResult> BuildContentUpdateAsync(PlayerBuildRequest request, string outputDirectory,
        IProgress<PlayerBuildProgress>? progress, CancellationToken cancellationToken) =>
        PlayerContentUpdatePipeline.BuildCoreAsync(request, outputDirectory, _assetBundleBuildPipeline,
            _assetDatabaseFactory, fileSystem, pathService, platformService, _buildCapabilities, progress, cancellationToken);
    public void SetLatestContentVersion(string directory, string version, CancellationToken token) =>
        PlayerContentUpdatePipeline.SetLatestVersionCore(directory, version, fileSystem, pathService, platformService, token);
}
