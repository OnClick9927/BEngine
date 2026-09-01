namespace BEngine.Editor;

internal sealed class AssetBundleBuildAsset
{
    internal required Guid Guid { get; init; }
    internal required Guid OwnerGuid { get; init; }
    internal required long LocalIdentifier { get; init; }
    internal required string Address { get; init; }
    internal required string Entry { get; init; }
    internal required string ArtifactPath { get; init; }
    internal required string AssetType { get; init; }
    internal required string Importer { get; init; }
    internal required IReadOnlyDictionary<string, string> ImporterSettings { get; init; }
    internal required string ArtifactHash { get; init; }
    internal required long Size { get; init; }
}
