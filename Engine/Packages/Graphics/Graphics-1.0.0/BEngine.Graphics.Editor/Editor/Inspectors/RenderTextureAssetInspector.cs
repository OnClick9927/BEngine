namespace BEngine.Editor;

internal sealed class RenderTextureAssetInspector : TextureAssetInspector, IEditorAssetInspector
{
    public override Type AssetType => typeof(RenderTexture);
    public override bool ReadOnly => false;
    public bool ShowImportSettings => false;

    public EditorInspectorResult DrawProperties(BAsset asset, SerializedObject serialized, IEditorInspectorGUI gui)
    {
        var texture = (RenderTexture)asset;
        gui.BeginChangeCheck();
        var width = Math.Max(1, gui.IntField("Width", texture.width));
        var height = Math.Max(1, gui.IntField("Height", texture.height));
        var format = (RenderTextureFormat)gui.EnumPopup("Format", texture.format);
        var useDepth = gui.Toggle("Depth Buffer", texture.useDepth);
        var autoGenerateMips = gui.Toggle("Auto Generate Mips", texture.autoGenerateMips);
        var filterMode = (TextureFilterMode)gui.EnumPopup("Filter Mode", texture.filterMode);
        var wrapMode = (TextureWrapMode)gui.EnumPopup("Wrap Mode", texture.wrapMode);
        var changed = gui.EndChangeCheck();
        if (changed)
        {
            Undo.RecordObject(texture, "Edit Render Texture");
            texture.Resize(width, height);
            texture.format = format;
            texture.useDepth = useDepth;
            texture.autoGenerateMips = autoGenerateMips;
            texture.filterMode = filterMode;
            texture.wrapMode = wrapMode;
            EditorUtility.SetDirty(texture);
        }
        return new(true, changed);
    }
}
