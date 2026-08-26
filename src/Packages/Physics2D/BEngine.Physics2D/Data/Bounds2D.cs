namespace BEngine.Physics2D;

public readonly record struct Bounds2D(Vector2 center, Vector2 size)
{
    public Vector2 extents => size / 2;
    public Vector2 min => center - extents;
    public Vector2 max => center + extents;
    public bool Contains(Vector2 point) =>
        point.x >= min.x && point.x <= max.x && point.y >= min.y && point.y <= max.y;
}
