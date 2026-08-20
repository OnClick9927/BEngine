namespace BEngine.Editor;

public sealed class AssetBundleBuildDefinition
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> AssetPaths { get; init; } = [];
    public IReadOnlyList<string> Dependencies { get; init; } = [];
}
