namespace BEngine.TiledMap;

public readonly record struct TileCell(
    TileCoordinate Position,
    int TileId,
    TileTransformFlags Transform = TileTransformFlags.None);
