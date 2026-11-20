using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
namespace BEngine.Editor;

[CustomEditor(typeof(BAsset), true)]
public class BAssetEditor : Editor
{
    private AssetImporter? _importer;
    private SerializedObject? _importerObject;

    protected AssetImporter? assetImporter => _importer;

    protected override void OnEnable() => ReloadImporter();

    protected override void OnDisable()
    {
        _importerObject?.Dispose();
        _importerObject = null;
        _importer = null;
    }

    public override void OnInspectorGUI()
    {
        if (target is not BAsset asset) return;

        DrawAssetInformationHeader(asset);
        var result = EditorAssetAdapters.GetInspector(asset)?.DrawProperties(asset, serializedObject, IMGUIInspectorGUI.Instance) ?? default;
        if (result.Changed || (!result.Handled && DrawDefaultInspector())) hasUnsavedChanges = true;
        DrawImportSettings();
        DrawApplyBar();
    }

    public override void SaveChanges()
    {
        serializedObject.ApplyModifiedProperties();
        if (target is BAsset asset && EditorUtility.IsDirty(asset))
            AssetDatabase.SaveAsset(asset);

        _importerObject?.ApplyModifiedProperties();
        if (_importer is { assetPath.Length: > 0 })
        {
            Undo.RegisterImporterUndo(_importer.assetPath, "Apply Import Settings");
            _importer.SaveAndReimport();
        }

        hasUnsavedChanges = false;
    }

    public override void DiscardChanges()
    {
        if (target is BAsset asset && EditorUtility.IsDirty(asset))
            AssetDatabase.RevertAsset(asset);
        _importer?.Revert();
        _importerObject?.Update();
        serializedObject.Update();
        hasUnsavedChanges = false;
    }

    protected void DrawApplyBar()
    {
        if (target is not BAsset asset || !AssetDatabase.Contains(asset)) return;
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

    protected void DrawImportSettings()
    {
        if (_importerObject is null || target is not BAsset asset) return;
        var inspector = EditorAssetAdapters.GetInspector(asset);
        if (inspector?.ShowImportSettings == false) return;

        GUILayout.Space(8);
        GUILayout.Label("Import Settings", EditorStyles.boldLabel);
        _importerObject.UpdateIfRequiredOrScript();
        var changedBefore = GUI.changed;
        var result = inspector?.DrawImportSettings(_importer!, _importerObject, IMGUIInspectorGUI.Instance) ?? default;
        if (!result.Handled)
            foreach (var property in _importerObject.GetVisibleProperties())
                EditorGUILayout.PropertyField(property, includeChildren: true);
        if (_importerObject.ApplyModifiedProperties() || result.Changed || GUI.changed != changedBefore)
            hasUnsavedChanges = true;
    }

    protected void DrawAssetInformationHeader(BAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var icon = EditorIconRegistry.GetIconPath(asset.GetType()) ??
                   EditorAssetIcons.GetIconPath(asset.sourcePath);
        GUILayout.Label(new GUIContent(asset.name, icon, asset.sourcePath), EditorStyles.inspectorTitlebar);
        DrawReadOnly("Type", string.IsNullOrWhiteSpace(asset.assetType)
            ? ObjectNames.NicifyVariableName(asset.GetType().Name)
            : asset.assetType);
        DrawReadOnly("Project Path", asset.assetPath);
        DrawReadOnly("Full Path", asset.sourcePath);
        DrawReadOnly("GUID", asset.guid);

        EditorAssetAdapters.GetInspector(asset)?.DrawInformation(asset, IMGUIInspectorGUI.Instance);
        if (EditorCapabilities.GetRequiredService<FileUtility>().FileExists(asset.sourcePath))
            DrawReadOnly("Size", EditorUtility.FormatBytes(
                EditorCapabilities.GetRequiredService<FileUtility>().GetFileLength(asset.sourcePath)));
    }

    private void ReloadImporter()
    {
        _importerObject?.Dispose();
        _importer = target is BAsset asset && !string.IsNullOrWhiteSpace(asset.assetPath)
            ? AssetImporter.GetAtPath(asset.assetPath)
            : null;
        _importerObject = _importer is null ? null : new SerializedObject(_importer);
    }

    private static void DrawReadOnly(string label, string value)
    {
        using var disabled = new EditorGUI.DisabledScope(true);
        EditorGUILayout.TextField(label, value ?? string.Empty);
    }
}
