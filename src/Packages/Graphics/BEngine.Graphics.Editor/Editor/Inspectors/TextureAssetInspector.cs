namespace BEngine.Editor;

internal class TextureAssetInspector : EditorAssetAdapter, IEditorAssetInspector, IEditorAssetPreview
{
    public override Type AssetType => typeof(Texture);
    public override IEditorAssetInspector Inspector => this;
    public override IEditorAssetPreview Preview => this;
    public AssetPreviewImage? GetImage(BAsset asset) => EditorAssetPreviewSources.GetImage(((Texture)asset).sourcePath);
    public string GetIcon(BAsset asset) => EditorBuiltinIcons.Assets.Image;
    public string GetInfo(BAsset asset)
    {
        var texture = (Texture)asset;
        return texture.width > 0 && texture.height > 0
            ? $"{texture.width} x {texture.height} | {texture.compressionFormat}"
            : $"Texture | {texture.compressionFormat}";
    }
    public EditorAssetPreviewContent GetContent(BAsset asset) => GetImage(asset) is { } image
        ? new(EditorAssetPreviewKind.Image, Image: image)
        : new(EditorAssetPreviewKind.Unavailable, GetIcon(asset), "Texture preview unavailable");

    public void DrawInformation(BAsset asset, IEditorInspectorGUI gui)
    {
        var texture = (Texture)asset;
        gui.ReadOnlyText("Dimensions", texture.width > 0 && texture.height > 0
            ? $"{texture.width} x {texture.height}" : "Unavailable");
    }

    public EditorInspectorResult DrawImportSettings(AssetImporter importer, SerializedObject serialized, IEditorInspectorGUI gui)
    {
        if (importer is not TextureImporter textureImporter) return default;
        Property(nameof(TextureImporter.textureType));
        Property(nameof(TextureImporter.sRGBTexture));
        Property(nameof(TextureImporter.alphaIsTransparency));
        Property(nameof(TextureImporter.isReadable));
        if (textureImporter.textureType == TextureImporterType.Sprite)
        {
            gui.Space(4);
            gui.Heading("Sprite");
            Property(nameof(TextureImporter.pixelsPerUnit));
            Property(nameof(TextureImporter.spritePivotX));
            Property(nameof(TextureImporter.spritePivotY));
        }
        gui.Space(4);
        gui.Heading("Sampling");
        Property(nameof(TextureImporter.filterMode));
        Property(nameof(TextureImporter.wrapMode));
        Property(nameof(TextureImporter.generateMipMaps));
        Property(nameof(TextureImporter.maxTextureSize));
        Property(nameof(TextureImporter.compressionFormat));
        gui.Space(4);
        gui.Heading("Platform Overrides (Player Build)");
        Property(nameof(TextureImporter.platformSettings));
        Property(nameof(AssetImporter.userData));
        return new(true, false);

        void Property(string name)
        {
            if (serialized.FindProperty(name) is { } property) gui.PropertyField(property, includeChildren: true);
        }
    }
}
