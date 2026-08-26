namespace BEngine.TiledMap;

[Flags]
public enum TileTransformFlags
{
    None = 0,
    FlipX = 1 << 0,
    FlipY = 1 << 1,
    Rotate90 = 1 << 2,
    Rotate180 = 1 << 3
}
