namespace BEngine.Editor;

public sealed class AssetBundleBuildDefinition
{
    public string Name { get; init; } = string.Empty;
    /// <summary>Build/update ownership. Resource addresses remain global at runtime.</summary>
    public string PackageName { get; init; } = "default";
    public IReadOnlyList<string> AssetPaths { get; init; } = [];
    public IReadOnlyList<string> Dependencies { get; init; } = [];
}
