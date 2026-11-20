namespace BEngine.Editor;

[RuntimeTypeAlias("BEngine.Editor.ProjectWindowUtil, BEngine.EditorWorkbench.Editor")]
[RuntimeTypeAlias("BEngine.Editor.ProjectWindowUtil, BEngine.EditorWorkbench.Editor, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null")]
public static class ProjectWindowUtil
{
    public static void CreateAsset(BObject asset, string pathName) => AssetDatabase.CreateAsset(asset, pathName);
    public static void ShowCreatedAsset(BObject asset) => EditorUtility.PingObject(asset);

    public static string? GetActiveFolderPath() => EditorBridge.Host?.ActiveProjectFolderPath;

    public static void ShowCreatedAsset(string assetPath, bool beginRename = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        EditorBridge.Host?.RevealProjectAsset(assetPath.Replace('\\', '/'), beginRename);
    }
}
