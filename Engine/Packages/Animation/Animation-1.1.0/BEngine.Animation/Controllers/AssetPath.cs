using BEngine.Serialization;

namespace BEngine;

internal static class AssetPath
{
    public static string Resolve(string path)
    {
        var paths = RuntimeServiceAccess.PathService;
        return paths.IsPathRooted(path)
            ? paths.GetFullPath(path)
            : paths.GetFullPath(paths.Combine(
                Application.dataPath,
                path.Replace('/', paths.DirectorySeparatorChar)));
    }
}
