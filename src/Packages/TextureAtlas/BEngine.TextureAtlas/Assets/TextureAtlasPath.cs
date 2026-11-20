namespace BEngine;

internal static class TextureAtlasPath
{
    internal static string Resolve(string path) => RuntimeAssetReferences.ResolvePath(path);
}
