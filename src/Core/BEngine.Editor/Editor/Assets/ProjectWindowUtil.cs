namespace BEngine.Editor;

public static class ProjectWindowUtil
{
    public static void CreateAsset(BObject asset, string pathName) => AssetDatabase.CreateAsset(asset, pathName);
    public static void ShowCreatedAsset(BObject asset) => EditorGUIUtility.PingObject(asset);

    public static string? GetActiveFolderPath() => EditorBridge.Host?.ActiveProjectFolderPath;

    public static void ShowCreatedAsset(string assetPath, bool beginRename = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        EditorBridge.Host?.RevealProjectAsset(assetPath.Replace('\\', '/'), beginRename);
    }
}
