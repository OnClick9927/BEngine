using BEngine.Platform;
using RuntimeTextAsset = BEngine.TextAsset;
using static BEngine.Editor.EditorAssetPreviewSources;

namespace BEngine.Editor;

/// <summary>Presentation of the shared authoring assets. Domain packages override their own types.</summary>
internal sealed class DefaultAssetPreview : EditorAssetAdapter, IEditorAssetPreview
{
    public override Type AssetType => typeof(BAsset);
    public override bool ReadOnly => false;
    public override IEditorAssetPreview Preview => this;
    public string GetIcon(BAsset asset) => asset switch
    {
        DefaultAsset value => IconFor(value),
        PrefabAsset => EditorBuiltinIcons.Assets.Prefab,
        Scene => EditorBuiltinIcons.Assets.Scene,
        Script => EditorBuiltinIcons.Assets.Script,
        RuntimeTextAsset => EditorBuiltinIcons.Assets.Text,
        _ => EditorIconRegistry.GetIconPath(asset.GetType()) ?? EditorBuiltinIcons.Assets.Default
    };
    public string GetInfo(BAsset asset) => asset switch
    {
        TextAsset value => TextInfo(GetTextPreview(value), value.assetType),
        AssemblyDefinitionAsset value => $"Assembly Definition | {value.definition.References.Count} references",
        DefaultAsset value => FileInfo(value),
        Script value => TextInfo(value.text, "Script"),
        RuntimeTextAsset value => TextInfo(value.text, "Text"),
        PrefabAsset value => $"{value.objectCount} GameObjects, {value.componentCount} Components",
        Scene value => $"{value.gameObjects.Count} GameObjects, {value.rootCount} roots",
        _ => ObjectNames.NicifyVariableName(asset.GetType().Name)
    };
    public EditorAssetPreviewContent GetContent(BAsset asset)
    {
        if (asset is TextAsset text) return new(EditorAssetPreviewKind.Text, Text: GetTextPreview(text));
        if (asset is DefaultAsset source)
        {
            var detail = EditorServices.GetRequiredService<FileUtility>().DirectoryExists(SourcePath(source))
                ? "Folder" : string.IsNullOrWhiteSpace(source.assetType) ? "Asset" : source.assetType;
            return new(EditorAssetPreviewKind.IconSummary, GetIcon(source), DisplayName(source), detail);
        }
        return asset switch
        {
            Script value => new(EditorAssetPreviewKind.Text, Text: value.text),
            RuntimeTextAsset value => new(EditorAssetPreviewKind.Text, Text: value.text),
            PrefabAsset value => new(EditorAssetPreviewKind.IconSummary, GetIcon(value), value.name,
                $"{value.objectCount} objects  |  {value.componentCount} components"),
            Scene value => new(EditorAssetPreviewKind.IconSummary, GetIcon(value), value.name,
                $"{value.gameObjects.Count} objects  |  {value.rootCount} roots"),
            _ => new(EditorAssetPreviewKind.IconSummary, GetIcon(asset), asset.name,
                ObjectNames.NicifyVariableName(asset.GetType().Name))
        };
    }
}
