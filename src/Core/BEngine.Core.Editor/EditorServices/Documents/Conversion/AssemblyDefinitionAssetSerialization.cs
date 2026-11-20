using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
using BEngine.Editor.Documents;

namespace BEngine.Editor;

internal static class AssemblyDefinitionAssetSerialization
{
    internal static AssemblyDefinitionAsset Restore(
        AssemblyDefinitionDocument document,
        string sourcePath)
    {
        var asset = new AssemblyDefinitionAsset
        {
            name = EditorCapabilities.GetRequiredService<PathUtility>().GetFileName(sourcePath),
            definition = document
        };
        RuntimeAssetReferences.BindSource(asset, sourcePath, asset.sourcePath, asset.artifactPath,
            asset.Id, "AssemblyDefinition");
        return asset;
    }

    internal static AssemblyDefinitionDocument Capture(AssemblyDefinitionAsset value) => value.definition;

    internal static AssemblyDefinitionAsset Load(string path) =>
        Restore(YamlUtility.Load<AssemblyDefinitionDocument>(path), path);

    internal static void Save(AssemblyDefinitionAsset asset, string path) =>
        YamlUtility.Save(Capture(asset), path);
}
