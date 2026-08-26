namespace BEngine.Editor;

[CustomEditor(typeof(Sprite))]
public sealed class SpriteEditor : Editor
{
    private string _message = string.Empty;

    public override void OnInspectorGUI()
    {
        var sprite = (Sprite)target;
        GUILayout.Label(new GUIContent(sprite.name, EditorBuiltinIcons.Assets.Image, sprite.assetPath),
            EditorStyles.inspectorTitlebar);

        var currentTexture = AssetDatabase.LoadAssetAtPath<BEngine.Texture>(sprite.Texture);
        var changedBefore = GUI.changed;
        var selectedTexture = EditorGUILayout.ObjectField("Texture", currentTexture,
            typeof(BEngine.Texture), allowSceneObjects: false) as BEngine.Texture;
        var textureFieldChanged = GUI.changed != changedBefore;
        var texturePath = textureFieldChanged
            ? AssetDatabase.GetAssetPath(selectedTexture)
            : sprite.Texture;
        if (currentTexture is null && sprite.Texture.Length > 0)
            texturePath = EditorGUILayout.TextField("Texture Path", texturePath);

        var nextPivot = EditorGUILayout.Vector2Field("Pivot", sprite.pivot);
        nextPivot = new Vector2(Mathf.Clamp01(nextPivot.x), Mathf.Clamp01(nextPivot.y));
        if (!texturePath.Equals(sprite.Texture, StringComparison.Ordinal) ||
            nextPivot != sprite.pivot)
        {
            sprite.Texture = texturePath.Replace('\\', '/').Trim();
            sprite.PivotX = (float)nextPivot.x;
            sprite.PivotY = (float)nextPivot.y;
            EditorUtility.SetDirty(sprite);
            hasUnsavedChanges = true;
            _message = string.Empty;
        }

        if (_message.Length > 0) EditorGUILayout.HelpBox(_message, MessageType.Error);
        DrawApplyBar(sprite);
    }

    public override void SaveChanges()
    {
        var sprite = (Sprite)target;
        try
        {
            sprite.Validate();
            if (AssetDatabase.SaveAsset(sprite))
            {
                hasUnsavedChanges = false;
                _message = string.Empty;
            }
        }
        catch (Exception exception)
        {
            _message = exception.Message;
        }
    }

    public override void DiscardChanges()
    {
        if (AssetDatabase.RevertAsset((Sprite)target))
        {
            hasUnsavedChanges = false;
            _message = string.Empty;
        }
    }

    private void DrawApplyBar(Sprite sprite)
    {
        if (!AssetDatabase.Contains(sprite)) return;
        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        using (new EditorGUI.DisabledScope(!hasUnsavedChanges || !EditorAssetWritePolicy.CanWrite))
        {
            if (GUILayout.Button("Revert", GUILayout.Width(72))) DiscardChanges();
            if (GUILayout.Button("Apply", GUILayout.Width(72))) SaveChanges();
        }
        GUILayout.EndHorizontal();
    }
}
