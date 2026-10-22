namespace BEngine.Physics2D;

public abstract class Collider2D : Component
{
    private PhysicsMaterial2D? _material;

    public bool isTrigger { get; set; }
    public bool usedByComposite { get; set; }
    public bool usedByEffector { get; set; }
    internal CompositeCollider2D? compositeOwner
    {
        get
        {
            var owner = usedByComposite && this is not CompositeCollider2D
                ? gameObject.GetComponentInParent<CompositeCollider2D>() : null;
            return owner is not null && ReferenceEquals(owner.attachedRigidbody, attachedRigidbody) ? owner : null;
        }
    }
    public Vector2 offset { get; set; } = Vector2.zero;
    public PhysicsMaterial2D? sharedMaterial { get => _material; set => _material = value; }
    public PhysicsMaterial2D? material { get => _material; set => _material = value; }
    public Fix64 density
    {
        get;
        set => field = Fix64.Max(Fix64.Zero, value);
    } = Fix64.One;
    public Rigidbody2D? attachedRigidbody => gameObject.GetComponentInParent<Rigidbody2D>();
    public Bounds2D bounds => PhysicsWorld2D.GetBounds(this);
    public Vector2 ClosestPoint(Vector2 position) => PhysicsWorld2D.GetClosestPoint(this, position);
    public bool OverlapPoint(Vector2 point) => PhysicsWorld2D.OverlapPoint(this, point);
    public ColliderDistance2D Distance(Collider2D collider) => Physics2D.Distance(this, collider);
    public int Raycast(Vector2 direction, RaycastHit2D[] results, Fix64 distance = default,
        ulong layerMask = ulong.MaxValue) =>
        Physics2D.Raycast(transform.position, direction,
            new ContactFilter2D { useTriggers = true, useLayerMask = true, layerMask = layerMask },
            results, distance);
}
