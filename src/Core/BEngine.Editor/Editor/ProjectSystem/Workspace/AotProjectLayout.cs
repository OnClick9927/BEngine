namespace BEngine.ProjectSystem.Editor;

/// <summary>
/// Defines the engine-owned AOT project assets that form the Player bootstrap boundary.
/// </summary>
public static class AotProjectLayout
{
    public const string AssetRoot = "Assets/Aot";
    public const string AssemblyDefinitionAssetPath = AssetRoot + "/AOT.asmdef.yaml";
    public const string SceneAssetPath = AssetRoot + "/AOT.scene.yaml";
    public const string UiAssetRoot = AssetRoot + "/UI";
    public const string UiDocumentAssetPath = UiAssetRoot + "/AOT.uxml";
    public const string UiStyleAssetPath = UiAssetRoot + "/AOT.uss";
    public const string UiControllerAssetPath = UiAssetRoot + "/AotStartupView.cs";
    public const string LogoAssetPath = UiAssetRoot + "/BEngine.png";

    public static bool IsAotAssetPath(string? assetPath)
    {
        var normalized = NormalizeAssetPath(assetPath);
        return normalized.Equals(AssetRoot, StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(AssetRoot + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when deleting or moving the path would remove an engine-owned AOT identity.
    /// </summary>
    public static bool IsProtectedAssetPath(string? assetPath)
    {
        var normalized = NormalizeAssetPath(assetPath);
        if (normalized.Length == 0) return false;
        return normalized.Equals(SceneAssetPath, StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(AssetRoot, StringComparison.OrdinalIgnoreCase) ||
               AssetRoot.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAssetPath(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return string.Empty;
        var parts = assetPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalized = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (normalized.Count > 0) normalized.RemoveAt(normalized.Count - 1);
                continue;
            }
            normalized.Add(part);
        }
        return string.Join('/', normalized);
    }
}
