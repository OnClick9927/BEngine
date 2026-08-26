using BEngine.ProjectSystem;

namespace BEngine.Player;

internal static class PlayerAssetEnvironment
{
    internal static void Initialize(ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        Application.dataPath = Path.GetFullPath(workspace.AssetsPath);
        BAsset.ClearLoadedAssets();
        TextureAtlasResolver.Clear();
    }
}
