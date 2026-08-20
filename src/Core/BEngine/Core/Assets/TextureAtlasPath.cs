namespace BEngine;

internal static class TextureAtlasPath
{
    internal static string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith($"Assets{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            var dataPath = Path.GetFullPath(Application.dataPath);
            var root = Path.GetFileName(dataPath).Equals("Assets", StringComparison.OrdinalIgnoreCase)
                ? Directory.GetParent(dataPath)?.FullName ?? Directory.GetCurrentDirectory()
                : Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(root, normalized));
        }
        return Path.GetFullPath(Path.Combine(Application.dataPath, normalized));
    }
}
