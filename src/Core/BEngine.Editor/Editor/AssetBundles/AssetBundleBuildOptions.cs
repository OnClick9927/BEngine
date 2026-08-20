namespace BEngine.Editor;

public sealed class AssetBundleBuildOptions
{
    public string PackageName { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0.0";
    public string OutputDirectory { get; init; } = string.Empty;
}
