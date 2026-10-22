namespace BEngine.Editor;

[CustomEditor(typeof(DefaultAsset), true)]
public sealed class DefaultAssetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var asset = (DefaultAsset)target;
        var sourcePath = string.IsNullOrWhiteSpace(asset.sourcePath)
            ? TryResolve(asset.assetPath)
            : asset.sourcePath;
        var icon = string.IsNullOrWhiteSpace(sourcePath)
            ? EditorAssetIcons.DefaultAsset
            : EditorHostServices.FileSystem.DirectoryExists(sourcePath)
                ? EditorAssetIcons.ClosedFolder
                : EditorAssetIcons.GetIconPath(sourcePath);

        var displayName = ProjectBrowserPath.DisplayName(asset.name, asset.assetPath, sourcePath);
        GUILayout.Label(new GUIContent(displayName, icon, sourcePath), EditorStyles.inspectorTitlebar);
        DrawReadOnly("Type", asset.assetType);
        DrawReadOnly("Project Path", asset.assetPath);
        DrawReadOnly("Full Path", sourcePath);
        DrawReadOnly("GUID", string.IsNullOrWhiteSpace(asset.guid) ? "Not imported" : asset.guid);
        if (!string.IsNullOrWhiteSpace(asset.packageId))
        {
            DrawReadOnly("Package", asset.packageId);
            DrawReadOnly("Version", asset.packageVersion);
        }

        if (EditorHostServices.FileSystem.DirectoryExists(sourcePath))
        {
            var directoryCount = EditorHostServices.FileSystem.EnumerateDirectories(
                sourcePath, "*", SearchOption.TopDirectoryOnly).Count();
            var fileCount = EditorHostServices.FileSystem.EnumerateFiles(
                sourcePath, "*", SearchOption.TopDirectoryOnly).Count();
            DrawReadOnly("Contains", $"{directoryCount} folders, {fileCount} files");
            DrawReadOnly("Modified", EditorHostServices.FileSystem.GetLastWriteTimeUtc(sourcePath)
                .ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        }
        else if (EditorHostServices.FileSystem.FileExists(sourcePath))
        {
            DrawReadOnly("Size", EditorUtility.FormatBytes(
                EditorHostServices.FileSystem.GetFileLength(sourcePath)));
            DrawReadOnly("Modified", EditorHostServices.FileSystem.GetLastWriteTimeUtc(sourcePath)
                .ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        }
    }

    private static void DrawReadOnly(string label, string value)
    {
        using var disabled = new EditorGUI.DisabledScope(true);
        EditorGUILayout.TextField(label, value ?? string.Empty);
    }

    private static string TryResolve(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return string.Empty;
        try { return AssetDatabase.ResolveAssetPath(assetPath); }
        catch { return assetPath; }
    }
}
