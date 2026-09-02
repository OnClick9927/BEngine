namespace BEngine.Editor;

[CustomEditor(typeof(RenderTexture))]
public sealed class RenderTextureEditor : BAssetEditor
{
    public override void OnInspectorGUI()
    {
        var texture = (RenderTexture)target;
        DrawAssetInformationHeader(texture);

        EditorGUI.BeginChangeCheck();
        var width = Math.Max(1, EditorGUILayout.IntField("Width", texture.width));
        var height = Math.Max(1, EditorGUILayout.IntField("Height", texture.height));
        var format = (RenderTextureFormat)EditorGUILayout.EnumPopup("Format", texture.format);
        var useDepth = EditorGUILayout.Toggle("Depth Buffer", texture.useDepth);
        var autoGenerateMips = EditorGUILayout.Toggle("Auto Generate Mips", texture.autoGenerateMips);
        var filterMode = (TextureFilterMode)EditorGUILayout.EnumPopup("Filter Mode", texture.filterMode);
        var wrapMode = (TextureWrapMode)EditorGUILayout.EnumPopup("Wrap Mode", texture.wrapMode);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(texture, "Edit Render Texture");
            texture.Resize(width, height);
            texture.format = format;
            texture.useDepth = useDepth;
            texture.autoGenerateMips = autoGenerateMips;
            texture.filterMode = filterMode;
            texture.wrapMode = wrapMode;
            EditorUtility.SetDirty(texture);
            hasUnsavedChanges = true;
        }

        DrawApplyBar();
    }
}
