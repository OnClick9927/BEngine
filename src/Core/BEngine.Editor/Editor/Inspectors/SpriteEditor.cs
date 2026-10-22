namespace BEngine.Editor;

[CustomEditor(typeof(Sprite))]
public sealed class SpriteEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var sprite = (Sprite)target;
        GUILayout.Label(new GUIContent(sprite.name, EditorBuiltinIcons.Assets.Image), EditorStyles.inspectorTitlebar);
        using (new EditorGUI.DisabledScope(true))
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(sprite.Texture);
            _ = EditorGUILayout.ObjectField("Texture", texture, typeof(Texture), false);
            _ = EditorGUILayout.Vector2Field("Pivot", sprite.pivot);
            _ = EditorGUILayout.TextField("Owner GUID", sprite.OwnerGuid);
            _ = EditorGUILayout.TextField("Local Identifier", sprite.LocalIdentifier.ToString());
        }
        EditorGUILayout.HelpBox("This Sprite is created by its Texture importer. Edit it from the source Texture.", MessageType.Info);
    }
}
