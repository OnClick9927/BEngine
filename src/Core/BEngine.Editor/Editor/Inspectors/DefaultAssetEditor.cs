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
        var icon = Directory.Exists(sourcePath)
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

        if (Directory.Exists(sourcePath))
        {
            var information = new DirectoryInfo(sourcePath);
            var entries = information.EnumerateFileSystemInfos().ToArray();
            DrawReadOnly("Contains", $"{entries.Count(entry => entry is DirectoryInfo)} folders, " +
                                     $"{entries.Count(entry => entry is FileInfo)} files");
            DrawReadOnly("Modified", information.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        else if (File.Exists(sourcePath))
        {
            var information = new FileInfo(sourcePath);
            DrawReadOnly("Size", EditorUtility.FormatBytes(information.Length));
            DrawReadOnly("Modified", information.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
        }
    }

    private static void DrawReadOnly(string label, string value)
    {
        using var disabled = new EditorGUI.DisabledScope(true);
        EditorGUILayout.TextField(label, value ?? string.Empty);
    }

    private static string TryResolve(string assetPath)
    {
        try { return AssetDatabase.ResolveAssetPath(assetPath); }
        catch { return assetPath; }
    }
}
