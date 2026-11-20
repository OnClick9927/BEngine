namespace BEngine.Editor;

/// <summary>The stable address and physical output of an editor-generated file sub-asset.</summary>
public readonly record struct GeneratedAssetArtifact(
    Guid OwnerGuid,
    long LocalIdentifier,
    string Reference,
    string ArtifactPath);
