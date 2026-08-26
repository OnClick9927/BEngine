namespace BEngine;

internal static class AssetReferencePath
{
    internal static string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        if (HasStableProjectPrefix(normalized, "Assets") || HasStableProjectPrefix(normalized, "Packages"))
        {
            var dataPath = Path.GetFullPath(Application.dataPath);
            var root = Path.GetFileName(dataPath).Equals("Assets", StringComparison.OrdinalIgnoreCase)
                ? Directory.GetParent(dataPath)?.FullName ?? Directory.GetCurrentDirectory()
                : Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(root, normalized));
        }
        return Path.GetFullPath(Path.Combine(Application.dataPath, normalized));
    }

    internal static string ToReference(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (string.IsNullOrWhiteSpace(Application.dataPath)) return fullPath.Replace('\\', '/');
        var assetsPath = Path.GetFullPath(Application.dataPath);
        if (!Path.GetFileName(assetsPath).Equals("Assets", StringComparison.OrdinalIgnoreCase))
            return fullPath.Replace('\\', '/');
        var projectRoot = Directory.GetParent(assetsPath)?.FullName ?? assetsPath;
        var packagesPath = Path.Combine(projectRoot, "Packages");
        if (!IsInside(fullPath, assetsPath) && !IsInside(fullPath, packagesPath))
            return fullPath.Replace('\\', '/');
        return Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
    }

    internal static string Key(string path) => Resolve(path).Replace('\\', '/');

    private static bool HasStableProjectPrefix(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string path, string directory) =>
        path.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
