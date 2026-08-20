using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor;

internal sealed record ProjectBrowserItem(
    string VirtualPath,
    string DisplayName,
    string SourcePath,
    string AssetType,
    bool IsDirectory,
    bool IsPackage,
    AssetRecord? Asset = null,
    string? PackageId = null,
    string? PackageVersion = null)
{
    internal string NormalizedPath => ProjectBrowserPath.Normalize(VirtualPath);
    internal string EffectiveDisplayName => ProjectBrowserPath.DisplayName(DisplayName, NormalizedPath, SourcePath);
    internal int Depth => NormalizedPath.Count(character => character == '/');

    internal string? ParentPath => ProjectBrowserPath.Parent(NormalizedPath);
}
