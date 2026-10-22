using BEngine.ProjectSystem;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.Editor;

public interface IAssetBundleBuildPipeline
{
    AssetBundleBuildResult Build(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        IEnumerable<AssetBundleBuildDefinition> definitions,
        AssetBundleBuildOptions options,
        IProgress<AssetBundleBuildProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<AssetBundleBuildResult> BuildAsync(
        ProjectWorkspace workspace,
        ProjectAssetDatabase assetDatabase,
        IEnumerable<AssetBundleBuildDefinition> definitions,
        AssetBundleBuildOptions options,
        IProgress<AssetBundleBuildProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
