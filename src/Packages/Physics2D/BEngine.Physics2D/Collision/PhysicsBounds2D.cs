namespace BEngine.Physics2D;

internal readonly record struct PhysicsBounds2D(Vector2 Min, Vector2 Max)
{
    internal Vector2 Center => (Min + Max) * Fix64.Half;

    internal bool Intersects(PhysicsBounds2D other) =>
        Min.x <= other.Max.x && Max.x >= other.Min.x &&
        Min.y <= other.Max.y && Max.y >= other.Min.y;
}
