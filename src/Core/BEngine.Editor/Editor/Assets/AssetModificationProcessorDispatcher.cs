using System.Reflection;

namespace BEngine.Editor;

internal static class AssetModificationProcessorDispatcher
{
    internal static void OnWillCreateAsset(string assetPath) =>
        AssetProcessorRegistry.InvokeCreate(assetPath);

    internal static string[] OnWillSaveAssets(string[] assetPaths)
    {
        return AssetProcessorRegistry.InvokeSave(assetPaths);
    }

    internal static AssetDeleteResult OnWillDeleteAsset(string assetPath, RemoveAssetOptions options)
    {
        return AssetProcessorRegistry.InvokeDelete(assetPath, options);
    }

    internal static AssetMoveResult OnWillMoveAsset(string oldPath, string newPath)
    {
        return AssetProcessorRegistry.InvokeMove(oldPath, newPath);
    }
}
