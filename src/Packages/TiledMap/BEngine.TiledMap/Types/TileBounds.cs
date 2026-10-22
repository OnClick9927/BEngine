namespace BEngine.TiledMap;

public readonly record struct TileBounds(TileCoordinate Min, TileCoordinate Max)
{
    public int Width => IsEmpty ? 0 : checked(Max.X - Min.X + 1);
    public int Height => IsEmpty ? 0 : checked(Max.Y - Min.Y + 1);
    public bool IsEmpty => Max.X < Min.X || Max.Y < Min.Y;

    public static TileBounds Empty => new(new TileCoordinate(0, 0), new TileCoordinate(-1, -1));

    public bool Contains(TileCoordinate position) => !IsEmpty &&
        position.X >= Min.X && position.X <= Max.X && position.Y >= Min.Y && position.Y <= Max.Y;
}
