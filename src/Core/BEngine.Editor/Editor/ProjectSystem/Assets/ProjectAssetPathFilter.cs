namespace BEngine.ProjectSystem.Editor;

internal static class ProjectAssetPathFilter
{
    // Metadata locks and atomic-write files are implementation details, never assets.
    internal static bool IsIgnored(string path)
    {
        var name = path.AsSpan(path.LastIndexOfAny(['/', '\\']) + 1);
        return name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
               name.Contains(".meta.lock", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("~", StringComparison.Ordinal) ||
               name.StartsWith(".", StringComparison.Ordinal) && name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }
}
