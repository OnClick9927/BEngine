namespace BEngine.Editor;

public readonly record struct AssetLoadContext(
    Guid Guid,
    string AssetPath,
    string SourcePath,
    string AssetType,
    string ArtifactPath = "")
{
    public string ImportedPath => string.IsNullOrWhiteSpace(ArtifactPath) ? SourcePath : ArtifactPath;
}
