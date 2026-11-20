using BEngine.AssetBundles;
using BEngine.ProjectSystem;

namespace BEngine.Editor.Build;

public interface IEditorHotUpdateSessionFactory
{
    BValueTask<IEditorHotUpdateSession?> CreateAsync(
        ProjectWorkspace workspace,
        IAssetBundleManager assetBundles,
        BCancellationToken cancellationToken = default);
}
