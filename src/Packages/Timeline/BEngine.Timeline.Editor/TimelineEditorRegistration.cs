namespace BEngine.Editor;

internal static class TimelineEditorRegistration
{
    [OnOpenAsset]
    private static bool OpenAsset(string path)
    {
        if (string.IsNullOrWhiteSpace(EditorApplication.projectPath) ||
            !path.EndsWith(".timeline.yaml", StringComparison.OrdinalIgnoreCase)) return false;
        var assetPath = EditorServices.GetRequiredService<PathUtility>()
            .GetRelativePath(EditorApplication.projectPath, path).Replace('\\', '/');
        if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
            !assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) return false;
        if (AssetDatabase.LoadAssetAtPath<TimelineAsset>(assetPath) is not { } asset) return false;
        TimelineWindow.Open(asset);
        return true;
    }

    [InitializeOnLoadMethod]
    private static void RegisterAssetTypes() =>
        AssetTypeRegistry.Register<TimelineAsset>(".timeline.yaml", "Timeline",
            context => TimelineAsset.Load(context.SourcePath), "Timeline.png");
}
