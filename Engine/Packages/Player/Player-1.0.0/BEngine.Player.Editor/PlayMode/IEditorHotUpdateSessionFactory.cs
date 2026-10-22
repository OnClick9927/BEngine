using BEngine.AssetBundles;
using BEngine.ProjectSystem;

namespace BEngine.Editor;

public interface IEditorHotUpdateSessionFactory
{
    BValueTask<IEditorHotUpdateSession?> CreateAsync(
        ProjectWorkspace workspace,
        IAssetBundleManager assetBundles,
        BCancellationToken cancellationToken = default);
}
