namespace BEngine.Editor;

internal readonly record struct GeneratedAssetArtifact(
    Guid OwnerGuid,
    long LocalIdentifier,
    string Reference,
    string ArtifactPath);
