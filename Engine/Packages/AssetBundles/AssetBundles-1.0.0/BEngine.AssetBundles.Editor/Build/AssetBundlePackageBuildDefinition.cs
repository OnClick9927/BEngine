namespace BEngine.Editor;

/// <summary>A build-time assetbundle package containing one or more local bundle definitions.</summary>
public sealed class AssetBundlePackageBuildDefinition
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<AssetBundleBuildDefinition> Bundles { get; init; } = [];
}
