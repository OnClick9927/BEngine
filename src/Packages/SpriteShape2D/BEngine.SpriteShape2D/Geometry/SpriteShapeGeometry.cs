using BEngine.Physics2D;

namespace BEngine;

public sealed class SpriteShapeGeometry
{
    public Vector2[] Outline { get; init; } = [];
    public Vector2[] FillVertices { get; init; } = [];
    public Vector2[] FillUv { get; init; } = [];
    public int[] FillTriangles { get; init; } = [];
    public Vector2[] EdgeVertices { get; init; } = [];
    public Vector2[] EdgeUv { get; init; } = [];
    public int[] EdgeTriangles { get; init; } = [];
    public SpriteShapeCornerGeometry[] Corners { get; init; } = [];
}

public sealed record SpriteShapeCornerGeometry(Vector2[] Vertices, Vector2[] Uv, string Texture, Color Color);

public static class SpriteShapeGeometryBuilder
{
    public static SpriteShapeGeometry Build(SpriteShapeAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        asset.Validate();
        var outline = Sample(asset);
        var (edgeVertices, edgeUv, edgeTriangles) = BuildEdge(asset, outline);
        Vector2[] fillVertices = [];
        int[] fillTriangles = [];
        if (asset.Closed)
        {
            var paths = new List<ColliderPath2D> { new() { points = outline.ToList() } };
            paths.AddRange(asset.Holes.Select(hole => new ColliderPath2D
            {
                isHole = true,
                points = hole.Points.ToList()
            }));
            var triangulation = PolygonGeometry2D.Triangulate(paths);
            fillVertices = triangulation.Vertices;
            fillTriangles = triangulation.Triangles;
        }
        return new SpriteShapeGeometry
        {
            Outline = outline,
            EdgeVertices = edgeVertices,
            EdgeUv = edgeUv,
            EdgeTriangles = edgeTriangles,
            FillVertices = fillVertices,
            FillUv = asset.Closed ? BuildFillUv(fillVertices, asset.FillUv) : [],
            FillTriangles = fillTriangles,
            Corners = BuildCorners(asset)
        };
    }

    private static SpriteShapeCornerGeometry[] BuildCorners(SpriteShapeAsset asset)
    {
        if (asset.CornerRules.Count == 0) return [];
        var corners = new List<SpriteShapeCornerGeometry>();
        var count = asset.Points.Count;
        var orientation = asset.Closed ? SignedArea(Sample(asset)) : Fix64.One;
        for (var index = asset.Closed ? 0 : 1; index < (asset.Closed ? count : count - 1); index++)
        {
            var point = asset.Points[index];
            if (point.Mode != SpriteShapeTangentMode.Linear) continue;
            var previous = asset.Points[(index + count - 1) % count];
            var next = asset.Points[(index + 1) % count];
            var incoming = point.Position - (previous.Mode == SpriteShapeTangentMode.Linear
                ? previous.Position : point.Position + point.LeftTangent);
            var outgoing = (next.Mode == SpriteShapeTangentMode.Linear ? next.Position : point.Position + point.RightTangent) - point.Position;
            if (incoming.sqrMagnitude <= Fix64.Epsilon || outgoing.sqrMagnitude <= Fix64.Epsilon) continue;
            incoming = incoming.normalized; outgoing = outgoing.normalized;
            var cross = Cross(incoming, outgoing);
            var angle = Fix64.Abs(Mathf.Atan2(cross, Vector2.Dot(incoming, outgoing)) * Fix64.Rad2Deg);
            if (angle <= Fix64.Parse("0.0001")) continue;
            var inner = cross * orientation < 0;
            var rule = asset.CornerRules.FirstOrDefault(rule => rule.InnerCorner == inner && angle >= rule.MinimumAngle && angle <= rule.MaximumAngle);
            if (rule is null) continue;
            var axis = incoming + outgoing;
            if (axis.sqrMagnitude <= Fix64.Epsilon) axis = outgoing;
            axis = axis.normalized * (rule.Size.x * Fix64.Half);
            var normal = new Vector2(-axis.y, axis.x).normalized * (rule.Size.y * Fix64.Half);
            var center = point.Position;
            corners.Add(new([center - axis - normal, center + axis - normal, center + axis + normal, center - axis + normal],
                [Uv(rule.Uv, 0, 0), Uv(rule.Uv, 1, 0), Uv(rule.Uv, 1, 1), Uv(rule.Uv, 0, 1)], rule.Texture, rule.Color));
        }
        return corners.ToArray();
    }

    public static Vector2[] Sample(SpriteShapeAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var count = asset.Points.Count;
        if (count == 0) return [];
        var segments = asset.Closed ? count : count - 1;
        var result = new List<Vector2>(segments * asset.SamplesPerSegment + (asset.Closed ? 0 : 1));
        for (var segment = 0; segment < segments; segment++)
        {
            var a = asset.Points[segment];
            var b = asset.Points[(segment + 1) % count];
            if (asset.AdaptiveTessellation &&
                (a.Mode != SpriteShapeTangentMode.Linear || b.Mode != SpriteShapeTangentMode.Linear))
            {
                result.Add(a.Position);
                Subdivide(a.Position, a.Position + a.RightTangent, b.Position + b.LeftTangent,
                    b.Position, asset.FlatnessTolerance, asset.MaxSubdivisionDepth, result);
                result.RemoveAt(result.Count - 1);
                continue;
            }
            for (var sample = 0; sample < asset.SamplesPerSegment; sample++)
            {
                var t = (Fix64)sample / asset.SamplesPerSegment;
                result.Add(Evaluate(a, b, t));
            }
        }
        if (!asset.Closed) result.Add(asset.Points[^1].Position);
        return result.ToArray();
    }

    private static void Subdivide(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
        Fix64 tolerance, int depth, ICollection<Vector2> result)
    {
        if (depth <= 0 || Fix64.Max(DistanceToLine(p1, p0, p3), DistanceToLine(p2, p0, p3)) <= tolerance)
        {
            result.Add(p3);
            return;
        }
        var p01 = (p0 + p1) * Fix64.Half;
        var p12 = (p1 + p2) * Fix64.Half;
        var p23 = (p2 + p3) * Fix64.Half;
        var p012 = (p01 + p12) * Fix64.Half;
        var p123 = (p12 + p23) * Fix64.Half;
        var middle = (p012 + p123) * Fix64.Half;
        Subdivide(p0, p01, p012, middle, tolerance, depth - 1, result);
        Subdivide(middle, p123, p23, p3, tolerance, depth - 1, result);
    }

    private static Fix64 DistanceToLine(Vector2 point, Vector2 from, Vector2 to)
    {
        var edge = to - from;
        if (edge.sqrMagnitude <= Fix64.Epsilon) return Vector2.Distance(point, from);
        return Fix64.Abs(Cross(edge, point - from)) / edge.magnitude;
    }

    public static Fix64 SignedArea(IReadOnlyList<Vector2> points)
    {
        var area = Fix64.Zero;
        for (var index = 0; index < points.Count; index++)
        {
            var next = points[(index + 1) % points.Count];
            area += points[index].x * next.y - next.x * points[index].y;
        }
        return area * Fix64.Half;
    }

    private static Vector2 Evaluate(SpriteShapePoint a, SpriteShapePoint b, Fix64 t)
    {
        if (a.Mode == SpriteShapeTangentMode.Linear && b.Mode == SpriteShapeTangentMode.Linear)
            return a.Position + (b.Position - a.Position) * t;
        var inverse = Fix64.One - t;
        var p0 = a.Position;
        var p1 = a.Position + a.RightTangent;
        var p2 = b.Position + b.LeftTangent;
        var p3 = b.Position;
        return p0 * (inverse * inverse * inverse) + p1 * (3 * inverse * inverse * t) +
               p2 * (3 * inverse * t * t) + p3 * (t * t * t);
    }

    private static (Vector2[] Vertices, Vector2[] Uv, int[] Triangles) BuildEdge(
        SpriteShapeAsset asset, IReadOnlyList<Vector2> outline)
    {
        var vertices = new List<Vector2>();
        var uv = new List<Vector2>();
        var triangles = new List<int>();
        var segments = asset.Closed ? outline.Count : outline.Count - 1;
        var phase = Fix64.Zero;
        var halfWidth = asset.EdgeWidth * Fix64.Half;
        for (var index = 0; index < segments; index++)
        {
            var from = outline[index];
            var to = outline[(index + 1) % outline.Count];
            var delta = to - from;
            var length = delta.magnitude;
            if (length <= Fix64.Epsilon) continue;
            var direction = delta / length;
            var normal = new Vector2(-direction.y, direction.x) * halfWidth;
            var consumed = Fix64.Zero;
            while (consumed < length)
            {
                var available = asset.EdgeRepeatLength - phase;
                var step = Fix64.Min(length - consumed, available);
                var start = from + direction * consumed;
                var end = from + direction * (consumed + step);
                var u0 = phase / asset.EdgeRepeatLength;
                var u1 = (phase + step) / asset.EdgeRepeatLength;
                AddQuad(start + normal, start - normal, end - normal, end + normal,
                    Uv(asset.EdgeUv, u0, 0), Uv(asset.EdgeUv, u0, 1),
                    Uv(asset.EdgeUv, u1, 1), Uv(asset.EdgeUv, u1, 0),
                    vertices, uv, triangles);
                consumed += step;
                phase += step;
                if (phase >= asset.EdgeRepeatLength - Fix64.Epsilon) phase = Fix64.Zero;
            }
        }
        return (vertices.ToArray(), uv.ToArray(), triangles.ToArray());
    }

    private static void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d,
        Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud,
        ICollection<Vector2> vertices, ICollection<Vector2> uv, ICollection<int> triangles)
    {
        var first = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
        uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
        triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
        triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
    }

    private static Vector2[] BuildFillUv(IReadOnlyList<Vector2> points, Rect rect)
    {
        var min = points.Aggregate(Vector2.Min);
        var max = points.Aggregate(Vector2.Max);
        var size = max - min;
        var width = Fix64.Max(size.x, Fix64.Parse("0.000001"));
        var height = Fix64.Max(size.y, Fix64.Parse("0.000001"));
        return points.Select(point => Uv(rect, (point.x - min.x) / width, (point.y - min.y) / height)).ToArray();
    }

    private static Vector2 Uv(Rect rect, Fix64 x, Fix64 y) =>
        new(rect.x + x * rect.width, rect.y + y * rect.height);

    private static int[] EarClip(IReadOnlyList<Vector2> points)
    {
        var indices = Enumerable.Range(0, points.Count).ToList();
        if (SignedArea(points) < 0) indices.Reverse();
        var triangles = new List<int>((points.Count - 2) * 3);
        var guard = points.Count * points.Count;
        while (indices.Count > 3 && guard-- > 0)
        {
            var clipped = false;
            for (var item = 0; item < indices.Count; item++)
            {
                var a = indices[(item + indices.Count - 1) % indices.Count];
                var b = indices[item];
                var c = indices[(item + 1) % indices.Count];
                if (Cross(points[b] - points[a], points[c] - points[b]) <= 0) continue;
                if (indices.Any(candidate => candidate != a && candidate != b && candidate != c &&
                                             Inside(points[candidate], points[a], points[b], points[c]))) continue;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                indices.RemoveAt(item);
                clipped = true;
                break;
            }
            if (!clipped) throw new InvalidDataException("Sprite shape outline is self-intersecting or cannot be triangulated.");
        }
        if (indices.Count != 3) throw new InvalidDataException("Sprite shape fill triangulation failed.");
        triangles.Add(indices[0]); triangles.Add(indices[1]); triangles.Add(indices[2]);
        return triangles.ToArray();
    }

    private static bool Inside(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        var ab = Cross(b - a, point - a);
        var bc = Cross(c - b, point - b);
        var ca = Cross(a - c, point - c);
        return ab >= 0 && bc >= 0 && ca >= 0;
    }

    private static Fix64 Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}
