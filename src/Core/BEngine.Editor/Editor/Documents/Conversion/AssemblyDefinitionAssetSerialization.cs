using BEngine.Editor.Documents;

namespace BEngine.Editor;

internal static class AssemblyDefinitionAssetSerialization
{
    internal static AssemblyDefinitionAsset Restore(
        AssemblyDefinitionDocument document,
        string sourcePath) => new()
    {
        name = Path.GetFileName(sourcePath),
        assetPath = sourcePath,
        assetType = "AssemblyDefinition",
        definition = document
    };

    internal static AssemblyDefinitionDocument Capture(AssemblyDefinitionAsset value) => value.definition;

    internal static AssemblyDefinitionAsset Load(string path) =>
        Restore(YamlUtility.Load<AssemblyDefinitionDocument>(path), path);

    internal static void Save(AssemblyDefinitionAsset asset, string path) =>
        YamlUtility.Save(Capture(asset), path);
}
