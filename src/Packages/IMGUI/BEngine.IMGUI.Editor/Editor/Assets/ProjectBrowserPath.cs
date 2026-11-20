using EditorCapabilities = BEngine.Editor.EditorServices;
using BEngine.Platform;
namespace BEngine.Editor;

internal static class ProjectBrowserPath
{
    internal static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        // Most callers already hold catalog paths. Keep their string and avoid
        // splitting/rejoining every segment during sorting, search and repaint.
        if (IsNormalized(path)) return path;
        return string.Join('/', path.Replace('\\', '/').Split('/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static bool IsNormalized(string path)
    {
        if (path[0] == '/' || path[^1] == '/' ||
            char.IsWhiteSpace(path[0]) || char.IsWhiteSpace(path[^1])) return false;
        var remaining = path.AsSpan();
        while (true)
        {
            var index = remaining.IndexOfAny('/', '\\');
            if (index < 0) return true;
            if (remaining[index] == '\\' || index == 0 || index == remaining.Length - 1 ||
                char.IsWhiteSpace(remaining[index - 1]) || char.IsWhiteSpace(remaining[index + 1])) return false;
            remaining = remaining[(index + 1)..];
        }
    }

    internal static string? Parent(string? path)
    {
        var normalized = Normalize(path);
        var index = normalized.LastIndexOf('/');
        return index <= 0 ? null : normalized[..index];
    }

    internal static string DisplayName(string? preferredName, string? virtualPath, string? sourcePath)
        => EditorAssetPreviewSources.DisplayName(preferredName, virtualPath, sourcePath);
}
