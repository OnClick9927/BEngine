using System.Reflection;

namespace BEngine.Editor;

internal static class AssetPostprocessorDispatcher
{
    internal static void Notify(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        var postprocessors = AssetProcessorRegistry.Postprocessors;

        foreach (var assetPath in importedAssets)
        {
            foreach (var descriptor in postprocessors)
            {
                if (!EditorFeatureGuard.TryInvoke<AssetPostprocessor?>(
                        $"Asset postprocessor {descriptor.Type.FullName}.CreateInstance",
                        descriptor.Factory, null, out var processor) || processor is null) continue;
                processor.assetPath = assetPath;
                processor.assetImporter = AssetImporter.GetAtPath(assetPath);
                Invoke(descriptor.Preprocess, processor, descriptor.Type, "OnPreprocessAsset");
                Invoke(descriptor.Postprocess, processor, descriptor.Type, "OnPostprocessAsset");
            }
        }

        foreach (var descriptor in postprocessors)
            Invoke(descriptor.PostprocessAll, importedAssets, deletedAssets, movedAssets,
                movedFromAssetPaths, descriptor.Type);
    }

    private static void Invoke(Action<AssetPostprocessor>? callback, AssetPostprocessor processor,
        Type type, string callbackName)
    {
        if (callback is null) return;
        EditorFeatureGuard.Invoke($"Asset postprocessor {type.FullName}.{callbackName}",
            () => callback(processor));
    }

    private static void Invoke(Action<string[], string[], string[], string[]>? callback,
        string[] imported, string[] deleted, string[] moved, string[] movedFrom, Type type)
    {
        if (callback is null) return;
        EditorFeatureGuard.Invoke($"Asset postprocessor {type.FullName}.OnPostprocessAllAssets",
            () => callback(imported, deleted, moved, movedFrom));
    }
}
