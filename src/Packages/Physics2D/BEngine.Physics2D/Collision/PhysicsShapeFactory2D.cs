namespace BEngine.Physics2D;

internal static partial class PhysicsShapeFactory2D
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PolygonCollider2D, PolygonCache> PolygonCaches = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Collider2D, PrimitiveCache> PrimitiveCaches = new();
    internal static PhysicsShape2D Create(Collider2D collider)
    {
        ArgumentNullException.ThrowIfNull(collider);
        if (collider is CircleCollider2D circle) return CreateCircle(circle);
        if (collider is PolygonCollider2D polygon)
        {
            var cached = PolygonCaches.GetOrCreateValue(polygon);
            if (cached.Shape is null || !cached.MatchesPaths(polygon))
            {
                var shape = polygon.holes.Count == 0 ? CreateConcavePolygon(polygon.points.ToArray())
                    : CreateRegion(PolygonGeometry2D.Fill(new[] { new ColliderPath2D { points = polygon.points } }.Concat(
                        polygon.holes.Select(hole => new ColliderPath2D { points = hole.points, isHole = true })).ToArray()));
                cached.CapturePaths(polygon);
                cached.Shape = shape; cached.WorldShape = null;
            }
            var matrix = polygon.transform.localToWorldMatrix;
            if (cached.WorldShape is not null && cached.WorldMatrix == matrix && cached.Offset == polygon.offset)
                return cached.WorldShape;
            cached.WorldShape = TransformShape(cached.Shape, polygon.transform, polygon.offset);
            cached.WorldMatrix = matrix; cached.Offset = polygon.offset;
            return cached.WorldShape;
        }
        if (collider is EdgeCollider2D edge) return CreateEdges(edge);
        if (collider is CompositeCollider2D composite)
            return TransformShape(GetCompositeShape(composite), composite.transform.TransformPoint);
        return CreatePrimitive(collider);
    }

    internal static PhysicsShape2D CreateCircle(Vector2 center, Fix64 radius)
    {
        radius = Fix64.Max(Fix64.Zero, radius);
        var extent = Vector2.one * radius;
        return new PhysicsShape2D
        {
            Type = PhysicsShapeType2D.Circle,
            Center = center,
            Radius = radius,
            Bounds = new PhysicsBounds2D(center - extent, center + extent)
        };
    }

    internal static PhysicsShape2D CreateBox(Vector2 center, Vector2 size, Fix64 angle)
    {
        var half = new Vector2(Fix64.Abs(size.x), Fix64.Abs(size.y)) * Fix64.Half;
        var vertices = new[]
        {
            Rotate(new Vector2(-half.x, -half.y), angle) + center,
            Rotate(new Vector2(half.x, -half.y), angle) + center,
            Rotate(new Vector2(half.x, half.y), angle) + center,
            Rotate(new Vector2(-half.x, half.y), angle) + center
        };
        return CreatePolygon(vertices);
    }

    private static PhysicsShape2D CreateCircle(CircleCollider2D collider)
    {
        var scale = collider.transform.lossyScale;
        var radius = Fix64.Max(Fix64.Zero, collider.radius * Mathf.Max(Fix64.Abs(scale.x), Fix64.Abs(scale.y)));
        var center = collider.transform.TransformPoint(collider.offset);
        var cache = PrimitiveCaches.GetOrCreateValue(collider);
        if (cache.Shape is { } shape && shape.Center == center && shape.Radius == radius) return shape;
        return cache.Shape = CreateCircle(center, radius);
    }

    private static PhysicsShape2D CreatePrimitive(Collider2D collider)
    {
        var matrix = collider.transform.localToWorldMatrix;
        var size = collider switch { BoxCollider2D box => box.size, CapsuleCollider2D capsule => capsule.size, _ => Vector2.one };
        var direction = collider is CapsuleCollider2D c ? c.direction : default;
        var cache = PrimitiveCaches.GetOrCreateValue(collider);
        if (cache.Shape is not null && cache.Matrix == matrix && cache.Offset == collider.offset &&
            cache.Size == size && cache.Direction == direction) return cache.Shape;
        // Published shapes stay immutable: retained queries, compound fixtures and
        // earlier solver phases may still refer to a previous world-space shape.
        var vertices = collider is CapsuleCollider2D capsuleCollider
            ? CapsuleVertices(capsuleCollider) : BoxVertices(collider, size);
        var shape = CreatePolygon(vertices);
        cache.Matrix = matrix; cache.Offset = collider.offset; cache.Size = size; cache.Direction = direction;
        return cache.Shape = shape;
    }

    private sealed class PrimitiveCache
    {
        internal PhysicsShape2D? Shape;
        internal AffineTransform2D Matrix;
        internal Vector2 Offset, Size;
        internal CapsuleDirection2D Direction;
    }

    private static Vector2[] BoxVertices(Collider2D collider, Vector2 size)
    {
        var half = size * Fix64.Half;
        return
        [
            collider.transform.TransformPoint(collider.offset + new Vector2(-half.x, -half.y)),
            collider.transform.TransformPoint(collider.offset + new Vector2(half.x, -half.y)),
            collider.transform.TransformPoint(collider.offset + new Vector2(half.x, half.y)),
            collider.transform.TransformPoint(collider.offset + new Vector2(-half.x, half.y))
        ];
    }

    private static PhysicsShape2D CreateEdges(EdgeCollider2D edge)
    {
        if (edge.edgeRadius <= Fix64.Zero) throw new InvalidDataException("EdgeCollider2D.edgeRadius must be positive.");
        using var budget = LoopExecutionGuard.AllowIterations(edge.points.Count + 1);
        var parts = new List<PhysicsShape2D>();
        for (var index = 1; index < edge.points.Count; index++)
        {
            var a = edge.points[index - 1] + edge.offset;
            var b = edge.points[index] + edge.offset;
            if (a == b) continue;
            var delta = (b - a).normalized * edge.edgeRadius;
            var normal = new Vector2(-delta.y, delta.x);
            parts.Add(CreatePolygon(new[] { a - delta - normal, b + delta - normal, b + delta + normal, a - delta + normal }
                .Select(edge.transform.TransformPoint).ToArray()));
        }
        return CreateCompound(parts);
    }

    internal static PhysicsShape2D CreateCompound(IEnumerable<PhysicsShape2D> shapes)
    {
        var parts = shapes.SelectMany(shape => shape.Type == PhysicsShapeType2D.Compound ? shape.Parts : new[] { shape }).ToArray();
        if (parts.Length == 1) return parts[0];
        var minimum = parts.Length == 0 ? Vector2.zero : parts.Select(part => part.Bounds.Min).Aggregate(Vector2.Min);
        var maximum = parts.Length == 0 ? Vector2.zero : parts.Select(part => part.Bounds.Max).Aggregate(Vector2.Max);
        return new PhysicsShape2D { Type = PhysicsShapeType2D.Compound, Parts = parts,
            Bounds = new PhysicsBounds2D(minimum, maximum), Center = (minimum + maximum) * Fix64.Half };
    }

    private static PhysicsShape2D CreateConcavePolygon(Vector2[] vertices)
    {
        var points = vertices.ToList();
        if (points.Count > 1 && points[0] == points[^1]) points.RemoveAt(points.Count - 1);
        if (points.Count < 3) return CreateCompound([]);
        if (points.Count > 4096) throw new InvalidDataException("A polygon path is limited to 4096 vertices.");
        using var budget = LoopExecutionGuard.AllowIterations(points.Count * points.Count * 8);
        // Remove zero-length and collinear edges before deterministic ear clipping.
        for (var index = points.Count - 1; index >= 0 && points.Count >= 3; index--)
        {
            var a = points[(index + points.Count - 1) % points.Count];
            var b = points[index]; var c = points[(index + 1) % points.Count];
            if (b == a || Cross(b - a, c - b) == Fix64.Zero) points.RemoveAt(index);
        }
        if (points.Count < 3) return CreateCompound([]);
        for (var left = 0; left < points.Count; left++)
            for (var right = left + 1; right < points.Count; right++)
            {
                if (right == left + 1 || left == 0 && right == points.Count - 1) continue;
                var a = points[left]; var b = points[(left + 1) % points.Count];
                var c = points[right]; var d = points[(right + 1) % points.Count];
                if (SegmentsIntersect(a, b, c, d)) throw new InvalidDataException("Polygon path self-intersects.");
            }
        if (SignedArea(points) < Fix64.Zero) points.Reverse();
        var convex = true;
        for (var index = 0; index < points.Count; index++)
            if (Cross(points[(index + 1) % points.Count] - points[index],
                    points[(index + 2) % points.Count] - points[(index + 1) % points.Count]) < Fix64.Zero) { convex = false; break; }
        if (convex) return CreatePolygon(points.ToArray());
        var triangles = new List<PhysicsShape2D>();
        while (points.Count > 3)
        {
            var removed = false;
            for (var index = 0; index < points.Count; index++)
            {
                var previous = (index + points.Count - 1) % points.Count; var next = (index + 1) % points.Count;
                var a = points[previous]; var b = points[index]; var c = points[next];
                if (Cross(b - a, c - b) <= Fix64.Zero) continue;
                var contains = false;
                for (var other = 0; other < points.Count; other++)
                {
                    if (other == previous || other == index || other == next) continue;
                    var p = points[other];
                    if (Cross(b - a, p - a) >= Fix64.Zero && Cross(c - b, p - b) >= Fix64.Zero && Cross(a - c, p - c) >= Fix64.Zero)
                    { contains = true; break; }
                }
                if (contains) continue;
                triangles.Add(CreatePolygon([a, b, c])); points.RemoveAt(index); removed = true; break;
            }
            if (!removed) throw new InvalidDataException("Polygon path is self-intersecting or degenerate.");
        }
        triangles.Add(CreatePolygon(points.ToArray()));
        return CreateCompound(triangles);
    }

    private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        if (Vector2.Max(a, b).x < Vector2.Min(c, d).x || Vector2.Max(c, d).x < Vector2.Min(a, b).x ||
            Vector2.Max(a, b).y < Vector2.Min(c, d).y || Vector2.Max(c, d).y < Vector2.Min(a, b).y) return false;
        var abC = Cross(b - a, c - a); var abD = Cross(b - a, d - a);
        var cdA = Cross(d - c, a - c); var cdB = Cross(d - c, b - c);
        return (abC <= Fix64.Zero && abD >= Fix64.Zero || abC >= Fix64.Zero && abD <= Fix64.Zero) &&
               (cdA <= Fix64.Zero && cdB >= Fix64.Zero || cdA >= Fix64.Zero && cdB <= Fix64.Zero);
    }

    private static Vector2[] CapsuleVertices(CapsuleCollider2D capsule)
    {
        const int ArcSegments = 6;
        var size = new Vector2(Fix64.Abs(capsule.size.x), Fix64.Abs(capsule.size.y));
        var vertical = capsule.direction == CapsuleDirection2D.Vertical;
        var radius = Fix64.Min(size.x, size.y) * Fix64.Half;
        var halfSegment = Fix64.Max(Fix64.Zero,
            (vertical ? size.y : size.x) * Fix64.Half - radius);
        var vertices = new Vector2[ArcSegments * 2 + 2];
        for (var index = 0; index <= ArcSegments; index++)
        {
            var angle = vertical
                ? 180 * index / ArcSegments
                : -90 + 180 * index / ArcSegments;
            var arc = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
            var offset = vertical ? new Vector2(0, halfSegment) : new Vector2(halfSegment, 0);
            vertices[index] = capsule.transform.TransformPoint(capsule.offset + offset + arc);
        }
        for (var index = 0; index <= ArcSegments; index++)
        {
            var angle = vertical
                ? 180 + 180 * index / ArcSegments
                : 90 + 180 * index / ArcSegments;
            var arc = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
            var offset = vertical ? new Vector2(0, -halfSegment) : new Vector2(-halfSegment, 0);
            vertices[ArcSegments + 1 + index] = capsule.transform.TransformPoint(capsule.offset + offset + arc);
        }
        return vertices;
    }

    private sealed class PolygonCache
    {
        internal PhysicsShape2D? Shape;
        internal PhysicsShape2D? WorldShape;
        internal AffineTransform2D WorldMatrix;
        internal Vector2 Offset;
        private Vector2[] _points = [];
        private int[] _pathCounts = [];
        private int _holeCount;

        internal bool MatchesPaths(PolygonCollider2D polygon)
        {
            if (polygon.holes.Count != _holeCount || polygon.points.Count != _pathCounts[0]) return false;
            var offset = 0;
            if (!Matches(polygon.points, ref offset)) return false;
            for (var i = 0; i < _holeCount; i++)
            {
                var points = polygon.holes[i].points;
                if (points.Count != _pathCounts[i + 1] || !Matches(points, ref offset)) return false;
            }
            return true;
        }

        private bool Matches(List<Vector2> points, ref int offset)
        {
            for (var i = 0; i < points.Count; i++) if (points[i] != _points[offset++]) return false;
            return true;
        }

        internal void CapturePaths(PolygonCollider2D polygon)
        {
            var total = polygon.points.Count;
            foreach (var hole in polygon.holes) total = checked(total + hole.points.Count);
            if (_points.Length < total) _points = new Vector2[total];
            if (_pathCounts.Length <= polygon.holes.Count) _pathCounts = new int[polygon.holes.Count + 1];
            _holeCount = polygon.holes.Count; _pathCounts[0] = polygon.points.Count;
            polygon.points.CopyTo(_points); var offset = polygon.points.Count;
            for (var i = 0; i < _holeCount; i++)
            {
                var points = polygon.holes[i].points;
                _pathCounts[i + 1] = points.Count; points.CopyTo(_points, offset); offset += points.Count;
            }
        }
    }

    private static PhysicsShape2D CreatePolygon(Vector2[] vertices)
    {
        using var budget = LoopExecutionGuard.AllowIterations(vertices.Length + 1);
        if (SignedArea(vertices) < Fix64.Zero) Array.Reverse(vertices);
        var minimum = vertices[0]; var maximum = minimum; var sum = Vector2.zero;
        for (var i = 0; i < vertices.Length; i++)
        {
            var point = vertices[i];
            minimum = Vector2.Min(minimum, point); maximum = Vector2.Max(maximum, point); sum += point;
        }
        var center = sum / vertices.Length;
        return new PhysicsShape2D
        {
            Type = PhysicsShapeType2D.Polygon,
            Center = center,
            Vertices = vertices,
            Bounds = new PhysicsBounds2D(minimum, maximum)
        };
    }

    private static Fix64 SignedArea(IReadOnlyList<Vector2> vertices)
    {
        var area = Fix64.Zero;
        for (var index = 0; index < vertices.Count; index++)
            area += Cross(vertices[index], vertices[(index + 1) % vertices.Count]);
        return area * Fix64.Half;
    }

    private static Vector2 Rotate(Vector2 value, Fix64 degrees)
    {
        var radians = degrees * Mathf.Deg2Rad;
        var cosine = Mathf.Cos(radians);
        var sine = Mathf.Sin(radians);
        return new Vector2(value.x * cosine - value.y * sine,
            value.x * sine + value.y * cosine);
    }

    private static Fix64 Cross(Vector2 left, Vector2 right) =>
        left.x * right.y - left.y * right.x;
}
