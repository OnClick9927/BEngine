namespace BEngine.Editor;

internal static class ProjectBrowserItemOrdering
{
    internal static ProjectBrowserItem[] Sort(IEnumerable<ProjectBrowserItem> items)
    {
        // Name projection strips extensions and can allocate. Capture it once;
        // retain the original stable index sorter and all existing tie-breakers.
        return items.Select(item => (Item: item, Name: item.EffectiveDisplayName))
            .OrderBy(entry => entry.Item.IsDirectory ? 0 : 1)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.Item.BrowserKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Item.BrowserKey, StringComparer.Ordinal)
            .Select(entry => entry.Item).ToArray();
    }
}
