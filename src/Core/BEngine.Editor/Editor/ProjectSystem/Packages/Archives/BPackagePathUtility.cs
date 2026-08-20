namespace BEngine.Editor;

internal static class BPackagePathUtility
{
    internal static string NormalizeRelativePath(string path, bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Path.IsPathRooted(path) || path.Contains('\0'))
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
                segment.Any(char.IsControl) || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidDataException($"Package path is invalid: '{path}'.");
            }
            if (!segment.Equals(segment.TrimEnd(' ', '.'), StringComparison.Ordinal) || IsReservedName(segment))
                throw new InvalidDataException($"Package path is not portable: '{path}'.");
        }
        return normalized;
    }

    internal static string ResolveInside(string root, string relativePath)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalized = NormalizeRelativePath(relativePath, allowEmpty: true);
        var resolved = Path.GetFullPath(Path.Combine(fullRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) &&
            !resolved.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Package path escapes its destination: '{relativePath}'.");
        }
        return resolved;
    }

    internal static string ResolveAssetSource(string assetsRoot, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        if (!Path.IsPathRooted(normalized) &&
            (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
             normalized.StartsWith($"Assets{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
        {
            normalized = normalized.Length == "Assets".Length
                ? string.Empty
                : normalized[("Assets".Length + 1)..];
        }
        var resolved = Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : ResolveInside(assetsRoot, normalized);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(assetsRoot));
        if (!resolved.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Only project Assets can be exported: '{path}'.");
        }
        return resolved;
    }

    internal static string ToAssetRelativePath(string assetsRoot, string fullPath)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(assetsRoot), Path.GetFullPath(fullPath))
            .Replace('\\', '/');
        return relative == "." ? string.Empty : NormalizeRelativePath(relative);
    }

    internal static void EnsureNoReparsePoint(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Path escapes its root: '{path}'.");
        }

        for (var current = fullPath; current.Length >= fullRoot.Length; current = Path.GetDirectoryName(current) ?? "")
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
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
