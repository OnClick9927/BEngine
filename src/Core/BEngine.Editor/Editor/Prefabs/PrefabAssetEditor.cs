namespace BEngine.Editor;

[CustomEditor(typeof(PrefabAsset))]
public sealed class PrefabAssetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var prefab = (PrefabAsset)target;
        GUILayout.Label(new GUIContent(prefab.name, EditorBuiltinIcons.Assets.Prefab, prefab.assetPath),
            EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Prefab Asset");
        EditorGUILayout.LabelField($"Objects: {prefab.objectCount}");
        EditorGUILayout.LabelField($"Components: {prefab.componentCount}");
        EditorGUILayout.LabelField(prefab.assetPath);
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("Open", EditorBuiltinIcons.Toolbar.OpenFolder,
                "Open this prefab in Prefab Mode"))) PrefabStageUtility.OpenPrefab(prefab.assetPath);
        if (GUILayout.Button(new GUIContent("Instantiate", EditorBuiltinIcons.Assets.Prefab,
                "Create a connected instance in the active Scene"))) PrefabUtility.InstantiatePrefab(prefab);
        GUILayout.EndHorizontal();
    }

    public override bool HasPreviewGUI() => true;
    public override string GetInfoString()
    {
        var prefab = (PrefabAsset)target;
        return $"{prefab.objectCount} GameObjects, {prefab.componentCount} Components";
    }
}
