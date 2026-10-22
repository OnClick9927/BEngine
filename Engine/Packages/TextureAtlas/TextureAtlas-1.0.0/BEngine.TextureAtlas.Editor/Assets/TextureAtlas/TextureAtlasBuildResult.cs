namespace BEngine.Editor;

public sealed record TextureAtlasBuildResult(
    string AtlasAssetPath,
    string TextureAssetPath,
    int Width,
    int Height,
    int SpriteCount);
