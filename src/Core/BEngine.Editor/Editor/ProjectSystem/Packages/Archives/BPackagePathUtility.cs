using BEngine.Platform;

namespace BEngine.Editor;

internal static class BPackagePathUtility
{
    internal static string NormalizeRelativePath(string path, bool allowEmpty = false) =>
        NormalizeRelativePath(
            path,
            EditorServiceAccess.GetRequiredService<PathUtility>(),
            allowEmpty);

    internal static string NormalizeRelativePath(
        string path,
        PathUtility pathService,
        bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(pathService);
        if (pathService.IsPathRooted(path) || path.Contains('\0'))
            throw new InvalidDataException($"Package path must be relative: '{path}'.");

        var normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0)
        {
            if (allowEmpty) return string.Empty;
            throw new InvalidDataException("Package path cannot be empty.");
        }

        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.Contains(':') ||
                segment.Any(char.IsControl) || segment.IndexOfAny(pathService.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidDataException($"Package path is invalid: '{path}'.");
            }
            if (!segment.Equals(segment.TrimEnd(' ', '.'), StringComparison.Ordinal) || IsReservedName(segment))
                throw new InvalidDataException($"Package path is not portable: '{path}'.");
        }
        return normalized;
    }

    internal static string ResolveInside(string root, string relativePath) =>
        ResolveInside(root, relativePath, EditorServiceAccess.GetRequiredService<PathUtility>());

    internal static string ResolveInside(
        string root,
        string relativePath,
        PathUtility pathService)
    {
        ArgumentNullException.ThrowIfNull(pathService);
        var fullRoot = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(root));
        var normalized = NormalizeRelativePath(relativePath, pathService, allowEmpty: true);
        var resolved = pathService.GetFullPath(pathService.Combine(fullRoot,
            normalized.Replace('/', pathService.DirectorySeparatorChar)));
        if (!resolved.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) &&
            !resolved.StartsWith(fullRoot + pathService.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Package path escapes its destination: '{relativePath}'.");
        }
        return resolved;
    }

    internal static string ResolveAssetSource(string assetsRoot, string path) =>
        ResolveAssetSource(assetsRoot, path, EditorServiceAccess.GetRequiredService<PathUtility>());

    internal static string ResolveAssetSource(
        string assetsRoot,
        string path,
        PathUtility pathService)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(pathService);
        var normalized = path.Replace('/', pathService.DirectorySeparatorChar)
            .Replace('\\', pathService.DirectorySeparatorChar);
        if (!pathService.IsPathRooted(normalized) &&
            (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
             normalized.StartsWith($"Assets{pathService.DirectorySeparatorChar}",
                 StringComparison.OrdinalIgnoreCase)))
        {
            normalized = normalized.Length == "Assets".Length
                ? string.Empty
                : normalized[("Assets".Length + 1)..];
        }
        var resolved = pathService.IsPathRooted(normalized)
            ? pathService.GetFullPath(normalized)
            : ResolveInside(assetsRoot, normalized, pathService);
        var root = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(assetsRoot));
        if (!resolved.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !resolved.StartsWith(root + pathService.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Only project Assets can be exported: '{path}'.");
        }
        return resolved;
    }

    internal static string ToAssetRelativePath(string assetsRoot, string fullPath) =>
        ToAssetRelativePath(assetsRoot, fullPath,
            EditorServiceAccess.GetRequiredService<PathUtility>());

    internal static string ToAssetRelativePath(
        string assetsRoot,
        string fullPath,
        PathUtility pathService)
    {
        ArgumentNullException.ThrowIfNull(pathService);
        var relative = pathService.GetRelativePath(
                pathService.GetFullPath(assetsRoot), pathService.GetFullPath(fullPath))
            .Replace('\\', '/');
        return relative == "." ? string.Empty : NormalizeRelativePath(relative, pathService);
    }

    internal static void EnsureNoReparsePoint(string root, string path) =>
        EnsureNoReparsePoint(
            root,
            path,
            EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PathUtility>());

    internal static void EnsureNoReparsePoint(
        string root,
        string path,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        var fullRoot = pathService.TrimEndingDirectorySeparator(pathService.GetFullPath(root));
        var fullPath = pathService.GetFullPath(path);
        if (!fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(fullRoot + pathService.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Path escapes its root: '{path}'.");
        }

        for (var current = fullPath;
             current.Length >= fullRoot.Length;
             current = pathService.GetDirectoryName(current) ?? "")
        {
            if ((fileSystem.FileExists(current) || fileSystem.DirectoryExists(current)) &&
                (fileSystem.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Package paths cannot pass through a link: '{current}'.");
            }
            if (current.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)) break;
        }
    }

    private static bool IsReservedName(string segment)
    {
        var name = segment.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                                    name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
               name[3] is >= '1' and <= '9';
    }
}
