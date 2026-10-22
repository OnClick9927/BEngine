namespace BEngine.Physics2D;

[AddComponentMenu("Physics 2D/Box Collider 2D")]
public sealed class BoxCollider2D : Collider2D
{
    public Vector2 size { get; set; } = Vector2.one;
}
