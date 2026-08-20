namespace BEngine.Physics2D;

[AddComponentMenu("Physics 2D/Polygon Collider 2D")]
public sealed class PolygonCollider2D : Collider2D
{
    public List<Vector2> points { get; set; } =
    [
        new(-Fix64.Half, -Fix64.Half),
        new(Fix64.Half, -Fix64.Half),
        new(Fix64.Half, Fix64.Half),
        new(-Fix64.Half, Fix64.Half)
    ];
}
