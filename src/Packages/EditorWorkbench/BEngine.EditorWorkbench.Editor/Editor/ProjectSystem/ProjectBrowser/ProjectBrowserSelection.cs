namespace BEngine.Editor;

internal static class ProjectBrowserSelection
{
    internal static DefaultAsset CreateReadOnlyAsset(ProjectBrowserItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return DefaultAsset.CreateReadOnlyReference(
            ProjectBrowserPath.DisplayName(item.DisplayName, item.NormalizedPath, item.SourcePath),
            item.NormalizedPath, item.SourcePath, item.AssetType, item.PackageId, item.PackageVersion);
    }
}
