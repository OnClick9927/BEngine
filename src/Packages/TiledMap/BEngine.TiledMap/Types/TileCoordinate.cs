namespace BEngine.TiledMap;

public readonly record struct TileCoordinate(int X, int Y) : IComparable<TileCoordinate>
{
    public static TileCoordinate zero => new(0, 0);

    public int CompareTo(TileCoordinate other)
    {
        var result = Y.CompareTo(other.Y);
        return result != 0 ? result : X.CompareTo(other.X);
    }

    public static TileCoordinate operator +(TileCoordinate left, TileCoordinate right) =>
        new(left.X + right.X, left.Y + right.Y);

    public static TileCoordinate operator -(TileCoordinate left, TileCoordinate right) =>
        new(left.X - right.X, left.Y - right.Y);
}
