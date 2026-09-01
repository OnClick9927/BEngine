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
    internal string BrowserKey { get; init; } = ProjectBrowserPath.Normalize(VirtualPath);
    internal string? BrowserParentKey { get; init; }
    internal BObject? SubAssetObject { get; init; }
    internal string? SubAssetIcon { get; init; }
    internal string NormalizedPath => ProjectBrowserPath.Normalize(VirtualPath);
    internal bool IsSubAsset => SubAssetObject is not null || Asset?.IsSubAsset == true;
    internal string EffectiveDisplayName
    {
        get
        {
            var displayName = ProjectBrowserPath.DisplayName(DisplayName, NormalizedPath, SourcePath);
            return IsDirectory || AssetType.Equals("Missing Package", StringComparison.OrdinalIgnoreCase)
                ? displayName
                : Path.GetFileNameWithoutExtension(displayName);
        }
    }
    internal int Depth => NormalizedPath.Count(character => character == '/');

    internal string? ParentPath => ProjectBrowserPath.Parent(NormalizedPath);
    internal string? TreeParentKey => BrowserParentKey ?? ParentPath;
}
