
namespace BEngine.Editor;

/// <summary>Immutable asset query information. Import state and database ownership remain private.</summary>
public readonly record struct EditorAssetInfo(
    Guid Guid,
    string AssetPath,
    string SourcePath,
    string ArtifactPath,
    string AssetType,
    bool IsDirectory,
    Guid? ParentGuid,
    long LocalIdentifier)
{
    public bool IsSubAsset => ParentGuid.HasValue;
}
