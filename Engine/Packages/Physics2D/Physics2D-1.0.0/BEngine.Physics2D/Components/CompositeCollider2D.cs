namespace BEngine.Physics2D;

public sealed class ColliderPath2D
{
    public List<Vector2> points { get; set; } = [];
    public bool isHole { get; set; }
}

/// <summary>A union of local polygon paths and child colliders marked usedByComposite.</summary>
[AddComponentMenu("Physics 2D/Composite Collider 2D")]
[DisallowMultipleComponent]
public class CompositeCollider2D : Collider2D
{
    public List<ColliderPath2D> paths { get; set; } = [];
    /// <summary>Combines the authored region with the union of eligible child colliders.</summary>
    public PolygonBooleanOperation2D operation { get; set; }
    public int circleSegments { get; set; } = 48;
    public virtual IReadOnlyList<ColliderPath2D> GetPaths() => paths;
    /// <summary>Actual result contours in local coordinates, including offset. Returns independent copies.</summary>
    public IReadOnlyList<ColliderPath2D> GetGeometryPaths() => PhysicsShapeFactory2D.GetCompositePaths(this);
}

[AddComponentMenu("Physics 2D/Edge Collider 2D")]
public sealed class EdgeCollider2D : Collider2D
{
    public List<Vector2> points { get; set; } = [new(-Fix64.Half, 0), new(Fix64.Half, 0)];
    /// <summary>Positive thickness provides stable solid contacts on both sides of the edge.</summary>
    public Fix64 edgeRadius { get; set; } = Fix64.FromDecimal(0.01m);
}
