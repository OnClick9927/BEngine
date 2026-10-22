using BEngine.Physics2D;

namespace BEngine;

[AddComponentMenu("Rendering/Sprite Shape 2D")]
[DisallowMultipleComponent]
public sealed class SpriteShapeRenderer2D : Renderer2D
{
    private long _collisionSignature = long.MinValue;

    public SpriteShapeAsset? shape { get; set; }
    public bool generateCollider { get; set; } = true;
    public Fix64 colliderEdgeRadius { get; set; } = Fix64.Parse("0.01");
    public Color color { get; set; } = Color.white;

    public SpriteShapeGeometry BuildGeometry() => SpriteShapeGeometryBuilder.Build(
        shape ?? throw new InvalidOperationException("SpriteShapeRenderer2D has no shape asset."));

    public bool RebuildCollider()
    {
        if (!generateCollider || shape is null) return false;
        var geometry = BuildGeometry();
        if (shape.Closed)
        {
            var polygon = gameObject.GetComponent<PolygonCollider2D>() ?? gameObject.AddComponent<PolygonCollider2D>();
            polygon.points = geometry.Outline.ToList();
            polygon.holes = shape.Holes.Select(hole => new ColliderPath2D
            {
                isHole = true,
                points = hole.Points.ToList()
            }).ToList();
            polygon.enabled = true;
            var edge = gameObject.GetComponent<EdgeCollider2D>();
            if (edge is not null) edge.enabled = false;
        }
        else
        {
            var edge = gameObject.GetComponent<EdgeCollider2D>() ?? gameObject.AddComponent<EdgeCollider2D>();
            edge.points = geometry.Outline.ToList();
            edge.edgeRadius = Fix64.Max(Fix64.Parse("0.0001"), colliderEdgeRadius);
            edge.enabled = true;
            var polygon = gameObject.GetComponent<PolygonCollider2D>();
            if (polygon is not null) polygon.enabled = false;
        }
        _collisionSignature = CollisionSignature(geometry.Outline, shape);
        return true;
    }

    public override void LateUpdate()
    {
        if (!generateCollider || shape is null) return;
        try
        {
            var outline = SpriteShapeGeometryBuilder.Sample(shape);
            var signature = CollisionSignature(outline, shape);
            if (signature != _collisionSignature) RebuildCollider();
        }
        catch (InvalidDataException) { }
    }

    public override void OnValidate()
    {
        colliderEdgeRadius = Fix64.Max(Fix64.Parse("0.0001"), colliderEdgeRadius);
        if (shape is null) return;
        shape.Validate();
        if (generateCollider) RebuildCollider();
    }

    private static long CollisionSignature(IReadOnlyList<Vector2> points, SpriteShapeAsset asset)
    {
        var hash = new HashCode();
        hash.Add(asset.Closed);
        hash.Add(points.Count);
        foreach (var point in points) { hash.Add(point.x.RawValue); hash.Add(point.y.RawValue); }
        foreach (var hole in asset.Holes)
        {
            hash.Add(hole.Points.Count);
            foreach (var point in hole.Points) { hash.Add(point.x.RawValue); hash.Add(point.y.RawValue); }
        }
        return hash.ToHashCode();
    }
}
