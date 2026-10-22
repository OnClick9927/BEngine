namespace BEngine.Editor;

public static class BuildPipeline
{
    public static AssetBundleBuildResult BuildAssetBundles(
        BEngine.ProjectSystem.ProjectWorkspace workspace,
        BEngine.ProjectSystem.Editor.AssetDatabase assetDatabase,
        AssetBundleBuildOptions options) => AssetBundleBuilder.Build(
            workspace, assetDatabase,
            AssetBundleBuildMap.FromImporters(workspace, assetDatabase), options);

    public static AssetBundleBuildResult BuildAssetBundles(
        BEngine.ProjectSystem.ProjectWorkspace workspace,
        BEngine.ProjectSystem.Editor.AssetDatabase assetDatabase,
        AssetBundleBuild[] builds,
        AssetBundleBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(builds);
        return AssetBundleBuilder.Build(workspace, assetDatabase,
            builds.Select(static build => build.ToDefinition()), options);
    }

    public static event Action<string>? buildStarted { add => EditorBuildEvents.buildStarted += value; remove => EditorBuildEvents.buildStarted -= value; }
    public static event Action<string, bool>? buildFinished { add => EditorBuildEvents.buildFinished += value; remove => EditorBuildEvents.buildFinished -= value; }
    internal static void RaiseBuildStarted(string path) => EditorBuildEvents.RaiseBuildStarted(path);
    internal static void RaiseBuildFinished(string path, bool succeeded) => EditorBuildEvents.RaiseBuildFinished(path, succeeded);
}
