namespace BEngine.Editor;

internal readonly record struct CreateAssetMenuEntry(
    Type AssetType,
    string MenuName,
    string FileName,
    int Order);
