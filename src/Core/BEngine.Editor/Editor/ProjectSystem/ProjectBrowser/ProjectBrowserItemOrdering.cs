namespace BEngine.Editor;

internal static class ProjectBrowserItemOrdering
{
    internal static ProjectBrowserItem[] Sort(IEnumerable<ProjectBrowserItem> items) => items
        .OrderBy(item => item.IsDirectory ? 0 : 1)
        .ThenBy(item => item.EffectiveDisplayName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(item => item.EffectiveDisplayName, StringComparer.Ordinal)
        .ThenBy(item => item.NormalizedPath, StringComparer.OrdinalIgnoreCase)
        .ThenBy(item => item.NormalizedPath, StringComparer.Ordinal)
        .ToArray();
}
