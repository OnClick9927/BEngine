namespace BEngine.Physics2D;

[AddComponentMenu("Physics 2D/Capsule Collider 2D")]
public sealed class CapsuleCollider2D : Collider2D
{
    public Vector2 size { get; set; } = new(1, 2);
    public CapsuleDirection2D direction { get; set; } = CapsuleDirection2D.Vertical;
}
