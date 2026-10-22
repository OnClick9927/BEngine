using Clipper2Lib;

namespace BEngine.Physics2D;

public enum PolygonBooleanOperation2D { Union, Intersection, Difference, Xor }

public readonly record struct PolygonTriangulation2D(Vector2[] Vertices, int[] Triangles);

/// <summary>Polygon clipping on a 1/1048576-unit grid. Inputs are not modified.</summary>
public static class PolygonGeometry2D
{
    private const long GridRaw = 4096;
    private const long MaximumCoordinate = 1_000_000L << 20;

    /// <summary>Combines two filled regions. Outer paths have isHole=false; holes have isHole=true.
    /// Paths use non-zero winding after orientation normalization. Holes must belong to an outer path.</summary>
    public static List<ColliderPath2D> Combine(IReadOnlyList<ColliderPath2D> subject,
        IReadOnlyList<ColliderPath2D> clip, PolygonBooleanOperation2D operation = PolygonBooleanOperation2D.Union) =>
        FromPaths(Apply(ToPaths(subject), ToPaths(clip), operation));

    /// <summary>Triangulates a filled region containing solid and hole contours.</summary>
    public static PolygonTriangulation2D Triangulate(IReadOnlyList<ColliderPath2D> paths)
    {
        var region = Fill(paths);
        if (region.Count == 0) return new([], []);
        var status = Clipper.Triangulate(region, out var triangles);
        if (status != TriangulateResult.success)
            throw new InvalidDataException($"Polygon triangulation failed: {status}.");
        var vertices = new List<Vector2>(triangles.Count * 3);
        var indices = new List<int>(triangles.Count * 3);
        foreach (var path in FromPaths(triangles))
        {
            if (path.points.Count != 3) continue;
            var start = vertices.Count;
            vertices.AddRange(path.points);
            indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
        }
        return new(vertices.ToArray(), indices.ToArray());
    }

    internal static Paths64 Apply(Paths64 subject, Paths64 clip, PolygonBooleanOperation2D operation) => operation switch
    {
        PolygonBooleanOperation2D.Union => Clipper.Union(subject, clip, FillRule.NonZero),
        PolygonBooleanOperation2D.Intersection => Clipper.Intersect(subject, clip, FillRule.NonZero),
        PolygonBooleanOperation2D.Difference => Clipper.Difference(subject, clip, FillRule.NonZero),
        PolygonBooleanOperation2D.Xor => Clipper.Xor(subject, clip, FillRule.NonZero),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    // Authored holes subtract from the union of solid paths, including overlapping solids.
    internal static Paths64 Fill(IReadOnlyList<ColliderPath2D> paths)
    {
        var solids = ToPaths(paths.Where(path => !path.isHole).ToArray());
        var holes = ToPaths(paths.Where(path => path.isHole).Select(path =>
            new ColliderPath2D { points = path.points }).ToArray());
        return Clipper.Difference(Clipper.Union(solids, FillRule.NonZero),
            Clipper.Union(holes, FillRule.NonZero), FillRule.NonZero);
    }

    internal static Paths64 ToPaths(IReadOnlyList<ColliderPath2D> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count > 1024 || paths.Sum(path => path?.points?.Count ?? 0) > 16384)
            throw new InvalidDataException("Polygon geometry is limited to 1024 paths and 16384 total vertices.");
        var result = new Paths64();
        foreach (var path in paths)
        {
            if (path?.points is null) throw new InvalidDataException("Polygon path cannot be null.");
            if (path.points.Count > 4096) throw new InvalidDataException("A polygon path is limited to 4096 vertices.");
            var points = new Path64(path.points.Select(point => new Point64(Quantize(point.x), Quantize(point.y))));
            while (points.Count > 1 && points[0] == points[^1]) points.RemoveAt(points.Count - 1);
            for (var i = points.Count - 1; i >= 0 && points.Count > 1; i--)
                if (points[i] == points[(i + 1) % points.Count]) points.RemoveAt(i);
            if (points.Count < 3) continue;
            using var budget = LoopExecutionGuard.AllowIterations(points.Count * points.Count * 2 + 1);
            for (var i = 0; i < points.Count; i++)
                for (var j = i + 1; j < points.Count; j++)
                {
                    if (j == i + 1 || i == 0 && j == points.Count - 1) continue;
                    if (Intersects(points[i], points[(i + 1) % points.Count], points[j], points[(j + 1) % points.Count]))
                        throw new InvalidDataException("Polygon path self-intersects.");
                }
            var area = Area(points);
            if (area == 0) continue;
            if ((area < 0) != path.isHole) points.Reverse();
            result.Add(points);
        }
        return result;
    }

    internal static List<ColliderPath2D> FromPaths(Paths64 paths) => paths.Select(path => new ColliderPath2D
    {
        isHole = Area(path) < 0,
        points = path.Select(point => new Vector2(Fix64.FromRaw(checked(point.X * GridRaw)),
            Fix64.FromRaw(checked(point.Y * GridRaw)))).ToList()
    }).ToList();

    internal static Vector2 Snap(Vector2 point) => new(Fix64.FromRaw(Quantize(point.x) * GridRaw),
        Fix64.FromRaw(Quantize(point.y) * GridRaw));

    private static long Quantize(Fix64 value)
    {
        var raw = (Int128)value.RawValue;
        var result = (long)((raw + (raw < 0 ? -GridRaw / 2 : GridRaw / 2)) / GridRaw);
        if (result < -MaximumCoordinate || result > MaximumCoordinate)
            throw new InvalidDataException("Polygon coordinates must be between -1000000 and 1000000 units.");
        return result;
    }

    private static Int128 Cross(Point64 a, Point64 b, Point64 c) =>
        (Int128)(b.X - a.X) * (c.Y - a.Y) - (Int128)(b.Y - a.Y) * (c.X - a.X);
    private static Int128 Area(Path64 path)
    {
        Int128 area = 0;
        for (var i = 0; i < path.Count; i++)
            area += (Int128)path[i].X * path[(i + 1) % path.Count].Y - (Int128)path[i].Y * path[(i + 1) % path.Count].X;
        return area;
    }
    private static bool Intersects(Point64 a, Point64 b, Point64 c, Point64 d)
    {
        if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < Math.Min(a.X, b.X) ||
            Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y) || Math.Max(c.Y, d.Y) < Math.Min(a.Y, b.Y)) return false;
        var abC = Cross(a, b, c); var abD = Cross(a, b, d); var cdA = Cross(c, d, a); var cdB = Cross(c, d, b);
        return (abC <= 0 && abD >= 0 || abC >= 0 && abD <= 0) && (cdA <= 0 && cdB >= 0 || cdA >= 0 && cdB <= 0);
    }
}
