namespace BEngine.Physics2D;

public readonly struct RaycastHit2D
{
    public Collider2D collider { get; init; }
    public Vector2 point { get; init; }
    public Vector2 normal { get; init; }
    public Fix64 distance { get; init; }
    public Rigidbody2D? rigidbody => collider?.attachedRigidbody;
    public Transform? transform => collider?.transform;
}
