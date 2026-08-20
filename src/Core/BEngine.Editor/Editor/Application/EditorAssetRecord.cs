
namespace BEngine.Editor;

internal readonly record struct EditorAssetRecord(
    Guid Guid,
    string AssetPath,
    string SourcePath,
    string AssetType,
    bool IsDirectory);
