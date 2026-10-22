namespace BEngine.Physics2D;

internal sealed class PhysicsShape2D
{
    internal PhysicsShapeType2D Type { get; init; }
    internal Vector2 Center { get; init; }
    internal Fix64 Radius { get; init; }
    internal Vector2[] Vertices { get; init; } = [];
    internal PhysicsBounds2D Bounds { get; init; }
    internal PhysicsShape2D[] Parts { get; init; } = [];
    internal ColliderPath2D[] Contours { get; init; } = [];
}
