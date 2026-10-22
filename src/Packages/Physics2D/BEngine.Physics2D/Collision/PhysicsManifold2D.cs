namespace BEngine.Physics2D;

internal readonly record struct PhysicsManifold2D(
    Vector2 Point,
    Vector2 Normal,
    Fix64 Penetration)
{
    internal Vector2 Point2 { get; init; }
    internal bool HasSecondPoint { get; init; }
}
