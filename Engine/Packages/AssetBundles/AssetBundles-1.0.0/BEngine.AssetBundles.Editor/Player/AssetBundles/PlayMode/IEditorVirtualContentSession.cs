using BEngine.AssetBundles;

namespace BEngine.Editor.Build;

public interface IEditorVirtualContentSession : IEditorPlayContentSession
{
    IAssetBundleManager AssetBundles { get; }
}
