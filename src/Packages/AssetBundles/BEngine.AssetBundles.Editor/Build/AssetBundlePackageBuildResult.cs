namespace BEngine.Editor;

public sealed record AssetBundlePackageBuildResult(
    string Name, int BuiltBundleCount, int ReusedBundleCount);
