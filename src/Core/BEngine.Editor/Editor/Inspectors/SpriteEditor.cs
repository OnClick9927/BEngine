namespace BEngine.Editor;

[CustomEditor(typeof(Sprite))]
public sealed class SpriteEditor : Editor
{
    private const string ImportedSpriteGuidance =
        "This Sprite is imported from a Texture. Select the source Texture and edit its Sprite settings in TextureImporter.";
    private string _message = string.Empty;

    public override void OnInspectorGUI()
    {
        var sprite = (Sprite)target;
        GUILayout.Label(new GUIContent(sprite.name, EditorBuiltinIcons.Assets.Image, sprite.assetPath),
            EditorStyles.inspectorTitlebar);

        if (!IsLegacySprite(sprite))
        {
            DrawImportedSprite(sprite);
            return;
        }

        var currentTexture = AssetDatabase.LoadAssetAtPath<BEngine.Texture>(sprite.Texture);
        var undoState = ObjectState.Capture(sprite);
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
            Undo.RegisterSnapshot(undoState, "Edit Sprite");
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
        if (!IsLegacySprite(sprite))
        {
            hasUnsavedChanges = false;
            EditorUtility.ClearDirty(sprite);
            _message = ImportedSpriteGuidance;
            return;
        }

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
        var sprite = (Sprite)target;
        if (!IsLegacySprite(sprite))
        {
            hasUnsavedChanges = false;
            EditorUtility.ClearDirty(sprite);
            _message = ImportedSpriteGuidance;
            return;
        }

        if (AssetDatabase.RevertAsset(sprite))
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

    private static bool IsLegacySprite(Sprite sprite) =>
        sprite.assetPath.EndsWith(".sprite.yaml", StringComparison.OrdinalIgnoreCase);

    private static void DrawImportedSprite(Sprite sprite)
    {
        using (new EditorGUI.DisabledScope(true))
        {
            var texture = AssetDatabase.LoadAssetAtPath<BEngine.Texture>(sprite.Texture);
            _ = EditorGUILayout.ObjectField("Texture", texture, typeof(BEngine.Texture), allowSceneObjects: false);
            _ = EditorGUILayout.Vector2Field("Pivot", sprite.pivot);
        }
        EditorGUILayout.HelpBox(ImportedSpriteGuidance, MessageType.Info);
    }
}
