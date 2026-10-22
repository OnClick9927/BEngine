namespace BEngine.Physics2D;

public readonly struct ColliderDistance2D
{
    public Vector2 pointA { get; init; }
    public Vector2 pointB { get; init; }
    public Vector2 normal { get; init; }
    public Fix64 distance { get; init; }
    public bool isOverlapped => distance < Fix64.Zero;
    public bool isValid { get; init; }
}
