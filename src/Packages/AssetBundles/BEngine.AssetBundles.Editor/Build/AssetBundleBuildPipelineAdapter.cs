using BEngine.ProjectSystem;
using BEngine.Platform;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.Editor;

public sealed class AssetBundleBuildPipelineAdapter : IAssetBundleBuildPipeline
{
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;

    public AssetBundleBuildPipelineAdapter(FileUtility fileSystem, PathUtility pathService)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
    }

    public AssetBundleBuildResult Build(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        IEnumerable<AssetBundleBuildDefinition> definitions,
        AssetBundleBuildOptions options,
        IProgress<AssetBundleBuildProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        AssetBundleBuilder.BuildCore(
            workspace, assetDatabase, definitions, options, _fileSystem, _pathService, progress, cancellationToken);

    public Task<AssetBundleBuildResult> BuildAsync(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        IEnumerable<AssetBundleBuildDefinition> definitions,
        AssetBundleBuildOptions options,
        IProgress<AssetBundleBuildProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        AssetBundleBuilder.BuildCoreAsync(
            workspace, assetDatabase, definitions, options, _fileSystem, _pathService, progress, cancellationToken);
}
