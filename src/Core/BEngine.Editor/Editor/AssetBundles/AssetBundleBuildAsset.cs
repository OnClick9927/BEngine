namespace BEngine.Editor;

internal sealed class AssetBundleBuildAsset
{
    internal required Guid Guid { get; init; }
    internal required string AssetPath { get; init; }
    internal required string SourcePath { get; init; }
    internal required string AssetType { get; init; }
    internal required string SourceHash { get; init; }
    internal required long Size { get; init; }
}
