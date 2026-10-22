namespace BEngine;

internal static class AssetReferencePath
{
    internal static string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var paths = RuntimeServiceAccess.PathService;
        if (paths.IsPathRooted(path)) return paths.GetFullPath(path);
        var normalized = path.Replace('/', paths.DirectorySeparatorChar);
        if (HasStableProjectPrefix(normalized, "Assets") || HasStableProjectPrefix(normalized, "Packages"))
        {
            var dataPath = paths.GetFullPath(Application.dataPath);
            var root = paths.GetFileName(dataPath).Equals("Assets", StringComparison.OrdinalIgnoreCase)
                ? paths.GetDirectoryName(dataPath) ?? RuntimeServiceAccess.PlatformService.CurrentDirectory
                : RuntimeServiceAccess.PlatformService.CurrentDirectory;
            return paths.GetFullPath(paths.Combine(root, normalized));
        }
        return paths.GetFullPath(paths.Combine(Application.dataPath, normalized));
    }

    internal static string ToReference(string path)
    {
        var paths = RuntimeServiceAccess.PathService;
        var fullPath = paths.GetFullPath(path);
        if (string.IsNullOrWhiteSpace(Application.dataPath)) return fullPath.Replace('\\', '/');
        var assetsPath = paths.GetFullPath(Application.dataPath);
        if (!paths.GetFileName(assetsPath).Equals("Assets", StringComparison.OrdinalIgnoreCase))
            return fullPath.Replace('\\', '/');
        var projectRoot = paths.GetDirectoryName(assetsPath) ?? assetsPath;
        var packagesPath = paths.Combine(projectRoot, "Packages");
        if (!IsInside(fullPath, assetsPath) && !IsInside(fullPath, packagesPath))
            return fullPath.Replace('\\', '/');
        return paths.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
    }

    internal static string Key(string path) => Resolve(path).Replace('\\', '/');

    private static bool HasStableProjectPrefix(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(prefix + RuntimeServiceAccess.PathService.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string path, string directory) =>
        path.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(directory + RuntimeServiceAccess.PathService.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
