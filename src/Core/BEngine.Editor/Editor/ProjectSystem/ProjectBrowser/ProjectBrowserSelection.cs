namespace BEngine.Editor;

internal static class ProjectBrowserSelection
{
    internal static DefaultAsset CreateReadOnlyAsset(ProjectBrowserItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new DefaultAsset
        {
            name = item.EffectiveDisplayName,
            assetPath = item.NormalizedPath,
            sourcePath = item.SourcePath,
            assetType = item.AssetType,
            packageId = item.PackageId ?? string.Empty,
            packageVersion = item.PackageVersion ?? string.Empty,
            hideFlags = HideFlags.NotEditable | HideFlags.DontSaveInEditor
        };
    }
}
