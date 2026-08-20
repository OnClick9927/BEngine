namespace BEngine.Physics2D;

public readonly struct ContactPoint2D
{
    public Vector2 point { get; init; }
    public Vector2 normal { get; init; }
    public Fix64 separation { get; init; }
    public Collider2D thisCollider { get; init; }
    public Collider2D otherCollider { get; init; }
}
