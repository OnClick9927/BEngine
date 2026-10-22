namespace BEngine.Physics2D;

[AddComponentMenu("Physics 2D/Polygon Collider 2D")]
public sealed class PolygonCollider2D : Collider2D
{
    /// <summary>Local cutout contours. Winding is ignored; overlapping holes are combined.</summary>
    public List<ColliderPath2D> holes { get; set; } = [];
    public List<Vector2> points { get; set; } =
    [
        new(-Fix64.Half, -Fix64.Half),
        new(Fix64.Half, -Fix64.Half),
        new(Fix64.Half, Fix64.Half),
        new(-Fix64.Half, Fix64.Half)
    ];
}
