namespace BEngine.Physics2D;

[AddComponentMenu("Physics 2D/Circle Collider 2D")]
public sealed class CircleCollider2D : Collider2D
{
    public Fix64 radius { get; set; } = Fix64.Half;
}
