namespace BEngine.Editor;

internal static class ProjectBrowserPath
{
    internal static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return string.Join('/', path.Replace('\\', '/').Split('/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    internal static string? Parent(string? path)
    {
        var normalized = Normalize(path);
        var index = normalized.LastIndexOf('/');
        return index <= 0 ? null : normalized[..index];
    }

    internal static string DisplayName(string? preferredName, string? virtualPath, string? sourcePath)
    {
        if (!string.IsNullOrWhiteSpace(preferredName)) return preferredName.Trim();
        var normalized = Normalize(virtualPath);
        if (normalized.Length > 0) return normalized[(normalized.LastIndexOf('/') + 1)..];
        if (string.IsNullOrWhiteSpace(sourcePath)) return "Unnamed";
        var trimmed = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed) is { Length: > 0 } name ? name : "Unnamed";
    }
}
