using BEngine.Serialization;

namespace BEngine.Animation;

internal static class AssetPath
{
    public static string Resolve(string path) => Path.IsPathRooted(path) ? path :
        Path.GetFullPath(Path.Combine(Application.dataPath, path.Replace('/', Path.DirectorySeparatorChar)));
}
