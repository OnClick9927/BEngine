
namespace BEngine.Editor;

internal readonly record struct EditorAssetRecord(
    Guid Guid,
    string AssetPath,
    string SourcePath,
    string ArtifactPath,
    string AssetType,
    bool IsDirectory,
    Guid? ParentGuid,
    long LocalIdentifier)
{
    internal bool IsSubAsset => ParentGuid.HasValue;
}
