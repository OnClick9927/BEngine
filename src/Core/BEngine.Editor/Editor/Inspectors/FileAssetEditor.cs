namespace BEngine.Editor;

[CustomEditor(typeof(FileAsset), true)]
public sealed class FileAssetEditor : Editor
{
    private AssetImporter? _importer;
    private SerializedObject? _importerObject;

    protected override void OnEnable() => ReloadImporter();

    protected override void OnDisable()
    {
        _importerObject?.Dispose();
        _importerObject = null;
        _importer = null;
    }

    public override void OnInspectorGUI()
    {
        var asset = (FileAsset)target;
        var icon = EditorIconRegistry.GetIconPath(asset.GetType()) ??
                   EditorAssetIcons.GetIconPath(asset.sourcePath);
        GUILayout.Label(new GUIContent(asset.name, icon, asset.sourcePath), EditorStyles.inspectorTitlebar);
        DrawReadOnly("Type", string.IsNullOrWhiteSpace(asset.assetType)
            ? ObjectNames.NicifyVariableName(asset.GetType().Name)
            : asset.assetType);
        DrawReadOnly("Project Path", asset.assetPath);
        DrawReadOnly("Full Path", asset.sourcePath);
        DrawReadOnly("GUID", asset.guid);
        DrawAssetInformation(asset);

        if (DrawDefaultInspector()) hasUnsavedChanges = true;

        if (_importerObject is null)
        {
            GUILayout.Space(6);
            GUILayout.Label("No importer settings", EditorStyles.miniLabel);
            return;
        }

        GUILayout.Space(8);
        GUILayout.Label("Import Settings", EditorStyles.boldLabel);
        _importerObject.UpdateIfRequiredOrScript();
        var changedBefore = GUI.changed;
        if (_importer is TextureImporter textureImporter)
            DrawTextureImporter(textureImporter, _importerObject);
        else
            foreach (var property in _importerObject.GetVisibleProperties())
                EditorGUILayout.PropertyField(property, includeChildren: true);
        if (_importerObject.ApplyModifiedProperties() || GUI.changed != changedBefore)
            hasUnsavedChanges = true;
        DrawApplyBar();
    }

    public override void SaveChanges()
    {
        serializedObject.ApplyModifiedProperties();
        if (target is BAsset asset && EditorUtility.IsDirty(asset)) AssetDatabase.SaveAsset(asset);
        _importerObject?.ApplyModifiedProperties();
        _importer?.SaveAndReimport();
        hasUnsavedChanges = false;
    }

    public override void DiscardChanges()
    {
        if (target is BAsset asset && EditorUtility.IsDirty(asset)) AssetDatabase.RevertAsset(asset);
        _importer?.Revert();
        _importerObject?.Update();
        hasUnsavedChanges = false;
    }

    private void ReloadImporter()
    {
        _importerObject?.Dispose();
        _importer = target is FileAsset asset && !string.IsNullOrWhiteSpace(asset.assetPath)
            ? AssetImporter.GetAtPath(asset.assetPath)
            : null;
        _importerObject = _importer is null ? null : new SerializedObject(_importer);
    }

    private void DrawApplyBar()
    {
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

    private static void DrawTextureImporter(TextureImporter importer, SerializedObject serialized)
    {
        DrawImporterProperty(serialized, nameof(TextureImporter.textureType));
        DrawImporterProperty(serialized, nameof(TextureImporter.sRGBTexture));
        DrawImporterProperty(serialized, nameof(TextureImporter.alphaIsTransparency));
        DrawImporterProperty(serialized, nameof(TextureImporter.isReadable));
        if (importer.textureType == TextureImporterType.Sprite)
        {
            GUILayout.Space(4);
            GUILayout.Label("Sprite", EditorStyles.boldLabel);
            DrawImporterProperty(serialized, nameof(TextureImporter.pixelsPerUnit));
            DrawImporterProperty(serialized, nameof(TextureImporter.spritePivotX));
            DrawImporterProperty(serialized, nameof(TextureImporter.spritePivotY));
        }
        GUILayout.Space(4);
        GUILayout.Label("Sampling", EditorStyles.boldLabel);
        DrawImporterProperty(serialized, nameof(TextureImporter.filterMode));
        DrawImporterProperty(serialized, nameof(TextureImporter.wrapMode));
        DrawImporterProperty(serialized, nameof(TextureImporter.generateMipMaps));
        DrawImporterProperty(serialized, nameof(TextureImporter.maxTextureSize));
        DrawImporterProperty(serialized, nameof(TextureImporter.compressionFormat));
        DrawImporterProperty(serialized, nameof(AssetImporter.userData));
    }

    private static void DrawImporterProperty(SerializedObject serialized, string name)
    {
        if (serialized.FindProperty(name) is { } property)
            EditorGUILayout.PropertyField(property, includeChildren: true);
    }

    private static void DrawAssetInformation(FileAsset asset)
    {
        switch (asset)
        {
            case BEngine.Texture texture:
                DrawReadOnly("Dimensions", texture.width > 0 && texture.height > 0
                    ? $"{texture.width} x {texture.height}"
                    : "Unavailable");
                break;
            case Script script:
                DrawReadOnly("Script Class", script.GetClass()?.FullName ?? "None");
                break;
        }
        if (!File.Exists(asset.sourcePath)) return;
        DrawReadOnly("Size", EditorUtility.FormatBytes(new FileInfo(asset.sourcePath).Length));
    }

    private static void DrawReadOnly(string label, string value)
    {
        using var disabled = new EditorGUI.DisabledScope(true);
        EditorGUILayout.TextField(label, value ?? string.Empty);
    }
}
