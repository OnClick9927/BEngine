using BEngine.AssetBundles;

namespace BEngine.Editor;

public interface IEditorVirtualContentSession : IDisposable
{
    IAssetBundleManager AssetBundles { get; }
    BValueTask PrepareAsync(BCancellationToken cancellationToken = default);
}
