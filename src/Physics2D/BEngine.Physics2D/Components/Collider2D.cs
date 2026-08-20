namespace BEngine.Physics2D;

public abstract class Collider2D : Component
{
    public bool isTrigger { get; set; }
    public Vector2 offset { get; set; } = Vector2.zero;
    public PhysicsMaterial2D? material { get; set; }
    public Rigidbody2D? attachedRigidbody => gameObject.GetComponent<Rigidbody2D>();
    public Bounds2D bounds => PhysicsWorld2D.GetBounds(this);
    public Vector2 ClosestPoint(Vector2 position) => PhysicsWorld2D.GetClosestPoint(this, position);
}
