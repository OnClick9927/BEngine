using Clipper2Lib;
using System.Runtime.CompilerServices;

namespace BEngine.Physics2D;

internal static partial class PhysicsShapeFactory2D
{
    private static readonly ConditionalWeakTable<CompositeCollider2D, CompositeCache> CompositeCaches = new();

    internal static IReadOnlyList<ColliderPath2D> GetCompositePaths(CompositeCollider2D collider) =>
        ClonePaths(GetCompositeShape(collider).Contours);

    private static PhysicsShape2D GetCompositeShape(CompositeCollider2D collider)
    {
        var authored = collider.GetPaths().Select(path => new ColliderPath2D
        { isHole = path.isHole, points = path.points.Select(point => PolygonGeometry2D.Snap(point + collider.offset)).ToList() }).ToArray();
        var children = new List<ColliderPath2D>();
        foreach (var source in collider.gameObject.GetComponentsInChildren<Collider2D>())
        {
            if (!source.enabled || !source.gameObject.activeInHierarchy || !ReferenceEquals(source.compositeOwner, collider)) continue;
            var shape = Create(source);
            foreach (var path in ShapePaths(shape, Math.Clamp(collider.circleSegments, 12, 256)))
                children.Add(new ColliderPath2D { isHole = path.isHole,
                    points = path.points.Select(point => PolygonGeometry2D.Snap(collider.transform.InverseTransformPoint(point))).ToList() });
        }
        var cache = CompositeCaches.GetOrCreateValue(collider);
        if (cache.Shape is not null && cache.Operation == collider.operation && EqualPaths(authored, cache.Authored) &&
            EqualPaths(children, cache.Children)) return cache.Shape;
        var subject = PolygonGeometry2D.Fill(authored);
        var clip = Clipper.Union(PolygonGeometry2D.ToPaths(children), FillRule.NonZero);
        var result = PolygonGeometry2D.Apply(subject, clip, collider.operation);
        var generated = CreateRegion(result);
        cache.Shape = generated; cache.Operation = collider.operation;
        cache.Authored = ClonePaths(authored); cache.Children = ClonePaths(children);
        return generated;
    }

    private static IEnumerable<ColliderPath2D> ShapePaths(PhysicsShape2D shape, int circleSegments)
    {
        if (shape.Contours.Length > 0) return shape.Contours;
        if (shape.Type == PhysicsShapeType2D.Compound) return shape.Parts.SelectMany(part => ShapePaths(part, circleSegments));
        if (shape.Type == PhysicsShapeType2D.Polygon) return [new ColliderPath2D { points = shape.Vertices.ToList() }];
        var points = new List<Vector2>();
        for (var i = 0; i < circleSegments; i++)
        {
            var angle = Fix64.TwoPi * i / circleSegments;
            points.Add(shape.Center + new Vector2(Fix64.Cos(angle), Fix64.Sin(angle)) * shape.Radius);
        }
        return [new ColliderPath2D { points = points }];
    }

    private static PhysicsShape2D CreateRegion(Paths64 region)
    {
        var contours = PolygonGeometry2D.FromPaths(region).ToArray();
        if (region.Count == 0) return CreateCompound([]);
        var status = Clipper.Triangulate(region, out var triangles);
        if (status != TriangulateResult.success) throw new InvalidDataException($"Polygon triangulation failed: {status}.");
        var pieces = MergeConvex(PolygonGeometry2D.FromPaths(triangles).Select(path => path.points.ToArray()));
        var shape = CreateCompound(pieces.Select(CreatePolygon));
        return WithContours(shape, contours);
    }

    // Merge adjacent triangles while their union remains convex. This removes artificial
    // diagonal contacts, and a rectangular union becomes a single four-vertex fixture.
    private static IEnumerable<Vector2[]> MergeConvex(IEnumerable<Vector2[]> triangles)
    {
        var pieces = triangles.Select(points => { if (SignedArea(points) < 0) Array.Reverse(points); return points; }).ToList();
        var edges = new Dictionary<(Vector2, Vector2), int>();
        var pending = new Queue<(Vector2, Vector2)>();
        void Add(int index)
        {
            var p = pieces[index];
            for (var e = 0; e < p.Length; e++) { var key = (p[e], p[(e + 1) % p.Length]); edges[key] = index; pending.Enqueue(key); }
        }
        void Remove(int index)
        {
            var p = pieces[index];
            for (var e = 0; e < p.Length; e++) edges.Remove((p[e], p[(e + 1) % p.Length]));
        }
        for (var i = 0; i < pieces.Count; i++) Add(i);
        using var budget = LoopExecutionGuard.AllowIterations(Math.Max(1024, pieces.Count * 128));
        while (pending.TryDequeue(out var edge))
        {
            if (!edges.TryGetValue(edge, out var a) || !edges.TryGetValue((edge.Item2, edge.Item1), out var b) || a == b) continue;
            var first = pieces[a]; var second = pieces[b];
            if (first.Length + second.Length > 18) continue;
            var merged = new List<Vector2>();
            var start = Array.IndexOf(first, edge.Item2);
            for (var i = 0; i < first.Length; i++) merged.Add(first[(start + i) % first.Length]);
            start = Array.IndexOf(second, edge.Item1);
            for (var i = 1; i < second.Length - 1; i++) merged.Add(second[(start + i) % second.Length]);
            var convex = true;
            for (var i = 0; i < merged.Count; i++)
                if (Cross(merged[(i + 1) % merged.Count] - merged[i], merged[(i + 2) % merged.Count] - merged[(i + 1) % merged.Count]) < 0)
                { convex = false; break; }
            if (!convex) continue;
            Remove(a); Remove(b); pieces[a] = merged.ToArray(); pieces[b] = []; Add(a);
        }
        return pieces.Where(piece => piece.Length > 0);
    }

    private static PhysicsShape2D WithContours(PhysicsShape2D shape, ColliderPath2D[] contours) => new()
    {
        Type = shape.Type, Center = shape.Center, Radius = shape.Radius, Vertices = shape.Vertices,
        Bounds = shape.Bounds, Parts = shape.Parts, Contours = contours
    };

    private static PhysicsShape2D TransformShape(PhysicsShape2D shape, Func<Vector2, Vector2> transform)
    {
        var result = shape.Type == PhysicsShapeType2D.Compound
            ? CreateCompound(shape.Parts.Select(part => TransformShape(part, transform)))
            : CreatePolygon(shape.Vertices.Select(transform).ToArray());
        return shape.Contours.Length == 0 ? result : WithContours(result, shape.Contours.Select(path => new ColliderPath2D
        { isHole = path.isHole, points = path.points.Select(transform).ToList() }).ToArray());
    }

    // Polygon authoring data is cached in local space. Keep the offset and world
    // transform in this overload so the caller can invalidate the transformed
    // result from a compact matrix signature without rebuilding the geometry.
    private static PhysicsShape2D TransformShape(PhysicsShape2D shape, Transform transform, Vector2 offset) =>
        TransformShape(shape, point => transform.TransformPoint(offset + point));

    private static ColliderPath2D[] ClonePaths(IEnumerable<ColliderPath2D> paths) => paths.Select(path =>
        new ColliderPath2D { isHole = path.isHole, points = path.points.ToList() }).ToArray();

    private static bool EqualPaths(IReadOnlyList<ColliderPath2D> a, IReadOnlyList<ColliderPath2D> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++) if (a[i].isHole != b[i].isHole || !a[i].points.SequenceEqual(b[i].points)) return false;
        return true;
    }

    private sealed class CompositeCache
    {
        internal ColliderPath2D[] Authored = [], Children = [];
        internal PolygonBooleanOperation2D Operation;
        internal PhysicsShape2D? Shape;
    }
}
