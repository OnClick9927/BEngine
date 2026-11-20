namespace BEngine.Editor;

/// <summary>One window's last grid projection; catalog records have init-only display inputs.</summary>
internal sealed class ProjectBrowserGridProjection
{
    private ProjectBrowserItem[] _sourceItems = [];
    private ProjectBrowserItem[] _visible = [];
    private string? _folder;
    private string? _search;
    private bool _valid;

    internal ProjectBrowserItem[] Get(IReadOnlyList<ProjectBrowserItem> items, string folder, string search)
    {
        // Check slots as well as count: callers may replace/reorder records in the
        // same array or list. No caller-owned array becomes our revision snapshot.
        if (_valid && string.Equals(folder, _folder, StringComparison.Ordinal) &&
            string.Equals(search, _search, StringComparison.Ordinal) && SameItems(items)) return _visible;

        var captured = new ProjectBrowserItem[items.Count];
        for (var index = 0; index < captured.Length; index++) captured[index] = items[index];
        IEnumerable<ProjectBrowserItem> candidates;
        if (string.IsNullOrWhiteSpace(search))
        {
            var normalizedFolder = ProjectBrowserPath.Normalize(folder);
            candidates = captured.Where(item => string.Equals(item.ParentPath, normalizedFolder,
                StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            var filter = ProjectSearchFilter.Parse(search);
            candidates = captured.Where(item =>
                !item.VirtualPath.Equals("Assets", StringComparison.OrdinalIgnoreCase) &&
                !item.VirtualPath.Equals("Packages", StringComparison.OrdinalIgnoreCase) && filter.Matches(item));
        }
        var visible = ProjectBrowserItemOrdering.Sort(candidates)
            .Where(item => !string.IsNullOrWhiteSpace(item.EffectiveDisplayName)).ToArray();
        // Failed projection never publishes a new cache identity.
        _sourceItems = captured;
        _folder = folder;
        _search = search;
        _visible = visible;
        _valid = true;
        return visible;
    }

    internal void Clear()
    {
        _valid = false;
        _sourceItems = [];
        _visible = [];
        _folder = null;
        _search = null;
    }

    private bool SameItems(IReadOnlyList<ProjectBrowserItem> items)
    {
        if (items.Count != _sourceItems.Length) return false;
        for (var index = 0; index < _sourceItems.Length; index++)
            if (!ReferenceEquals(items[index], _sourceItems[index])) return false;
        return true;
    }
}
