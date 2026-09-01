using System.Security.Cryptography;
using BEngine.Serialization;

namespace BEngine.ProjectSystem.Editor;

public sealed record AssetRecord(
    Guid Guid,
    string AssetPath,
    string SourcePath,
    string MetaPath,
    string ArtifactPath,
    string AssetType,
    string SourceHash,
    bool IsDirectory,
    Guid? ParentGuid = null,
    long LocalIdentifier = 0)
{
    public bool IsSubAsset => ParentGuid.HasValue;

    /// <summary>SHA-256 of the imported artifact represented by <see cref="ArtifactPath"/>.</summary>
    public string ArtifactHash { get; init; } = string.Empty;

    /// <summary>Byte length of the imported artifact represented by <see cref="ArtifactPath"/>.</summary>
    public long ArtifactSize { get; init; }
}
