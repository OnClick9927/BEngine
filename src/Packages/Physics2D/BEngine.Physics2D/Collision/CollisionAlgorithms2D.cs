namespace BEngine.Physics2D;

internal static partial class CollisionAlgorithms2D
{
    internal static PhysicsShape2D[] Leaves(PhysicsShape2D shape)
    {
        if (shape.Type != PhysicsShapeType2D.Compound) return [shape];

        // Flatten directly into the final-sized array. SelectMany/ToArray used to
        // allocate an iterator for every compound level and an intermediate buffer
        // before producing the array consumed by the solver.
        var leaves = new PhysicsShape2D[CountLeaves(shape)];
        var index = 0;
        FillLeaves(shape, leaves, ref index);
        return leaves;
    }

    private static int CountLeaves(PhysicsShape2D shape)
    {
        if (shape.Type != PhysicsShapeType2D.Compound) return 1;
        var count = 0;
        foreach (var part in shape.Parts) count += CountLeaves(part);
        return count;
    }

    private static void FillLeaves(PhysicsShape2D shape, PhysicsShape2D[] output, ref int index)
    {
        if (shape.Type != PhysicsShapeType2D.Compound)
        {
            output[index++] = shape;
            return;
        }
        foreach (var part in shape.Parts) FillLeaves(part, output, ref index);
    }
    internal readonly record struct ShapeContact(PhysicsManifold2D Manifold, int LeftPart, int RightPart);
    internal static ShapeContact[] CollectContacts(PhysicsShape2D left, PhysicsShape2D right)
    {
        var collector = new ContactCollector();
        return collector.Collect(left, right).ToArray();
    }

    // A world owns this scratch storage; contacts are copied into its persistent solver state.
    internal sealed partial class ContactCollector
    {
        private readonly List<PhysicsShape2D> _left = [], _right = [];
        private readonly List<ShapeContact> _contacts = [];
        internal void Clear() { _left.Clear(); _right.Clear(); _contacts.Clear(); }
        internal List<ShapeContact> Collect(PhysicsShape2D left, PhysicsShape2D right)
        {
            Clear();
            if (!left.Bounds.Intersects(right.Bounds)) return _contacts;
            AddLeaves(left, _left); AddLeaves(right, _right);
            for (var i = 0; i < _left.Count; i++) for (var j = 0; j < _right.Count; j++)
            {
                if (!TryCollide(_left[i], _right[j], out var contact)) continue;
                var duplicate = false;
                foreach (var existing in _contacts)
                    if (Vector2.Dot(existing.Manifold.Normal, contact.Normal) > (Fix64).999 &&
                        (existing.Manifold.Point - contact.Point).sqrMagnitude < (Fix64).000001 &&
                        existing.Manifold.HasSecondPoint == contact.HasSecondPoint &&
                        (!contact.HasSecondPoint || (existing.Manifold.Point2 - contact.Point2).sqrMagnitude < (Fix64).000001))
                    { duplicate = true; break; }
                if (!duplicate) _contacts.Add(new(contact, i, j));
            }
            return _contacts;
        }
        private static void AddLeaves(PhysicsShape2D shape, List<PhysicsShape2D> output)
        {
            if (shape.Type != PhysicsShapeType2D.Compound) { output.Add(shape); return; }
            foreach (var part in shape.Parts) AddLeaves(part, output);
        }
    }
    internal static bool TryCollide(
        PhysicsShape2D left,
        PhysicsShape2D right,
        out PhysicsManifold2D manifold)
    {
        using var budget = LoopExecutionGuard.AllowIterations(Math.Max(Math.Max(left.Parts.Length, right.Parts.Length), Math.Max(left.Vertices.Length, right.Vertices.Length)) + 1);
        if (!left.Bounds.Intersects(right.Bounds))
        {
            manifold = default;
            return false;
        }
        if (left.Type == PhysicsShapeType2D.Compound || right.Type == PhysicsShapeType2D.Compound)
        {
            manifold = default;
            var found = false;
            var leftParts = left.Type == PhysicsShapeType2D.Compound ? left.Parts : new[] { left };
            var rightParts = right.Type == PhysicsShapeType2D.Compound ? right.Parts : new[] { right };
            foreach (var a in leftParts)
                foreach (var b in rightParts)
                    if (TryCollide(a, b, out var candidate) && (!found || candidate.Penetration > manifold.Penetration))
                    { manifold = candidate; found = true; }
            return found;
        }
        if (left.Type == PhysicsShapeType2D.Circle && right.Type == PhysicsShapeType2D.Circle)
            return CircleCircle(left, right, out manifold);
        if (left.Type == PhysicsShapeType2D.Circle)
            return CirclePolygon(left, right, out manifold);
        if (right.Type == PhysicsShapeType2D.Circle)
        {
            var result = CirclePolygon(right, left, out manifold);
            manifold = manifold with { Normal = -manifold.Normal };
            return result;
        }
        return PolygonPolygon(left, right, out manifold);
    }

    internal static Vector2 ClosestPoint(PhysicsShape2D shape, Vector2 point)
    {
        using var budget = LoopExecutionGuard.AllowIterations(Math.Max(shape.Parts.Length, shape.Vertices.Length) + 1);
        if (shape.Type == PhysicsShapeType2D.Compound)
        {
            var closestPart = point;
            var bestPart = Fix64.FromRaw(long.MaxValue);
            foreach (var part in shape.Parts)
            {
                var candidate = ClosestPoint(part, point); var distance = (candidate - point).sqrMagnitude;
                if (distance < bestPart) { bestPart = distance; closestPart = candidate; }
            }
            return closestPart;
        }
        if (shape.Type == PhysicsShapeType2D.Circle)
        {
            var delta = point - shape.Center;
            return delta.sqrMagnitude <= shape.Radius * shape.Radius || delta.sqrMagnitude <= Fix64.Epsilon
                ? point
                : shape.Center + delta.normalized * shape.Radius;
        }
        if (Contains(shape, point)) return point;
        var closest = shape.Vertices[0];
        var best = Fix64.FromRaw(long.MaxValue);
        for (var index = 0; index < shape.Vertices.Length; index++)
        {
            var candidate = ClosestPointOnSegment(point, shape.Vertices[index],
                shape.Vertices[(index + 1) % shape.Vertices.Length]);
            var distance = (candidate - point).sqrMagnitude;
            if (distance >= best) continue;
            best = distance;
            closest = candidate;
        }
        return closest;
    }

    internal static bool Contains(PhysicsShape2D shape, Vector2 point)
    {
        using var budget = LoopExecutionGuard.AllowIterations(Math.Max(shape.Parts.Length, shape.Vertices.Length) + 1);
        if (shape.Type == PhysicsShapeType2D.Compound)
        {
            foreach (var part in shape.Parts)
                if (Contains(part, point)) return true;
            return false;
        }
        if (shape.Type == PhysicsShapeType2D.Circle)
            return (point - shape.Center).sqrMagnitude <= shape.Radius * shape.Radius;
        for (var index = 0; index < shape.Vertices.Length; index++)
        {
            var edge = shape.Vertices[(index + 1) % shape.Vertices.Length] - shape.Vertices[index];
            if (Cross(edge, point - shape.Vertices[index]) < Fix64.Zero) return false;
        }
        return true;
    }

    internal static bool Raycast(PhysicsShape2D shape, Vector2 origin, Vector2 direction,
        Fix64 maxDistance, out Fix64 distance, out Vector2 normal)
    {
        using var budget = LoopExecutionGuard.AllowIterations(Math.Max(shape.Parts.Length, shape.Vertices.Length) + 1);
        if (shape.Type == PhysicsShapeType2D.Compound)
        {
            distance = Fix64.FromRaw(long.MaxValue); normal = Vector2.zero;
            var found = false;
            foreach (var part in shape.Parts)
                if (Raycast(part, origin, direction, maxDistance, out var partDistance, out var partNormal) && partDistance < distance)
                { found = true; distance = partDistance; normal = partNormal; }
            return found;
        }
        if (shape.Type == PhysicsShapeType2D.Circle)
            return RaycastCircle(shape, origin, direction, maxDistance, out distance, out normal);
        if (Contains(shape, origin))
        {
            distance = Fix64.Zero;
            normal = -direction;
            return true;
        }
        var closest = Fix64.FromRaw(long.MaxValue);
        var hitNormal = Vector2.zero;
        for (var index = 0; index < shape.Vertices.Length; index++)
        {
            var from = shape.Vertices[index];
            var to = shape.Vertices[(index + 1) % shape.Vertices.Length];
            if (!RaySegment(origin, direction, from, to, out var rayTime) ||
                rayTime < Fix64.Zero || rayTime > maxDistance || rayTime >= closest) continue;
            closest = rayTime;
            var edge = to - from;
            hitNormal = new Vector2(edge.y, -edge.x).normalized;
            if (Vector2.Dot(hitNormal, direction) > Fix64.Zero) hitNormal = -hitNormal;
        }
        distance = closest;
        normal = hitNormal;
        return closest != Fix64.FromRaw(long.MaxValue);
    }

    internal static bool SweepCircle(
        Vector2 origin,
        Fix64 radius,
        Vector2 direction,
        Fix64 maxDistance,
        PhysicsShape2D target,
        out Fix64 distance,
        out Vector2 normal)
    {
        using var budget = LoopExecutionGuard.AllowIterations(Math.Max(target.Parts.Length, target.Vertices.Length) + 1);
        if (target.Type == PhysicsShapeType2D.Compound)
        {
            distance = Fix64.FromRaw(long.MaxValue); normal = Vector2.zero;
            var found = false;
            foreach (var part in target.Parts)
                if (SweepCircle(origin, radius, direction, maxDistance, part, out var partDistance, out var partNormal) && partDistance < distance)
                { found = true; distance = partDistance; normal = partNormal; }
            return found;
        }
        var query = PhysicsShapeFactory2D.CreateCircle(origin, radius);
        if (TryCollide(query, target, out var initial))
        {
            distance = Fix64.Zero;
            normal = initial.Normal;
            return true;
        }

        if (target.Type == PhysicsShapeType2D.Circle)
        {
            var expanded = PhysicsShapeFactory2D.CreateCircle(
                target.Center, target.Radius + radius);
            if (!RaycastCircle(expanded, origin, direction, maxDistance,
                    out distance, out var outward))
            {
                normal = default;
                return false;
            }
            normal = -outward;
            return true;
        }

        var closest = Fix64.FromRaw(long.MaxValue);
        var hitNormal = Vector2.zero;
        for (var index = 0; index < target.Vertices.Length; index++)
        {
            var from = target.Vertices[index];
            var to = target.Vertices[(index + 1) % target.Vertices.Length];
            var edge = to - from;
            if (edge.sqrMagnitude <= Fix64.Epsilon) continue;

            var outward = new Vector2(edge.y, -edge.x).normalized;
            var approach = Vector2.Dot(direction, outward);
            if (approach < -Fix64.Epsilon)
            {
                var signedDistance = Vector2.Dot(origin - from, outward);
                var rayTime = (radius - signedDistance) / approach;
                if (rayTime >= Fix64.Zero && rayTime <= maxDistance && rayTime < closest)
                {
                    var center = origin + direction * rayTime;
                    var contact = center - outward * radius;
                    var edgeTime = Vector2.Dot(contact - from, edge) / edge.sqrMagnitude;
                    if (edgeTime >= Fix64.Zero && edgeTime <= Fix64.One)
                    {
                        closest = rayTime;
                        hitNormal = -outward;
                    }
                }
            }

            var corner = PhysicsShapeFactory2D.CreateCircle(from, radius);
            if (!RaycastCircle(corner, origin, direction, maxDistance,
                    out var cornerTime, out var cornerOutward) || cornerTime >= closest) continue;
            closest = cornerTime;
            hitNormal = -cornerOutward;
        }

        distance = closest;
        normal = hitNormal;
        return closest != Fix64.FromRaw(long.MaxValue);
    }

    internal static bool SweepShape(PhysicsShape2D moving, Vector2 direction, Fix64 maxDistance,
        PhysicsShape2D target, out Fix64 distance, out Vector2 normal)
    {
        distance = Fix64.FromRaw(long.MaxValue);
        normal = Vector2.zero;
        if (direction.sqrMagnitude <= Fix64.Epsilon || maxDistance <= Fix64.Zero) return false;
        direction = direction.normalized;
        if (moving.Type == PhysicsShapeType2D.Compound || target.Type == PhysicsShapeType2D.Compound)
        {
            var movingParts = moving.Type == PhysicsShapeType2D.Compound ? moving.Parts : [moving];
            var targetParts = target.Type == PhysicsShapeType2D.Compound ? target.Parts : [target];
            var found = false;
            foreach (var left in movingParts)
                foreach (var right in targetParts)
                    if (SweepShape(left, direction, maxDistance, right, out var candidate, out var candidateNormal) &&
                        candidate < distance)
                    {
                        found = true;
                        distance = candidate;
                        normal = candidateNormal;
                    }
            return found;
        }
        if (moving.Type == PhysicsShapeType2D.Circle)
            return SweepCircle(moving.Center, moving.Radius, direction, maxDistance, target, out distance, out normal);
        if (target.Type == PhysicsShapeType2D.Circle)
        {
            if (!SweepCircle(target.Center, target.Radius, -direction, maxDistance, moving, out distance, out normal))
                return false;
            normal = -normal;
            return true;
        }
        if (TryCollide(moving, target, out var initial))
        {
            distance = Fix64.Zero;
            normal = initial.Normal;
            return true;
        }

        var enter = Fix64.Zero;
        var exit = maxDistance;
        var enterNormal = Vector2.zero;
        if (!TestSweepAxes(moving, target, direction, ref enter, ref exit, ref enterNormal) ||
            !TestSweepAxes(target, moving, direction, ref enter, ref exit, ref enterNormal)) return false;
        if (exit < Fix64.Zero || enter > maxDistance) return false;
        distance = Fix64.Max(Fix64.Zero, enter);
        normal = enterNormal.sqrMagnitude <= Fix64.Epsilon ? direction : enterNormal;
        return true;
    }

    private static bool TestSweepAxes(PhysicsShape2D source, PhysicsShape2D other,
        Vector2 direction, ref Fix64 enter, ref Fix64 exit, ref Vector2 enterNormal)
    {
        for (var index = 0; index < source.Vertices.Length; index++)
        {
            var edge = source.Vertices[(index + 1) % source.Vertices.Length] - source.Vertices[index];
            if (edge.sqrMagnitude <= Fix64.Epsilon) continue;
            var axis = new Vector2(-edge.y, edge.x).normalized;
            Project(source.Vertices, axis, out var sourceMin, out var sourceMax);
            Project(other.Vertices, axis, out var otherMin, out var otherMax);
            var speed = Vector2.Dot(direction, axis);
            if (Fix64.Abs(speed) <= Fix64.Epsilon)
            {
                if (sourceMax < otherMin || otherMax < sourceMin) return false;
                continue;
            }
            var first = (otherMin - sourceMax) / speed;
            var last = (otherMax - sourceMin) / speed;
            var axisNormal = speed > Fix64.Zero ? axis : -axis;
            if (first > last) (first, last) = (last, first);
            if (first > enter) { enter = first; enterNormal = axisNormal; }
            exit = Fix64.Min(exit, last);
            if (enter > exit) return false;
        }
        return true;
    }

    private static void Project(IReadOnlyList<Vector2> vertices, Vector2 axis, out Fix64 minimum, out Fix64 maximum)
    {
        minimum = maximum = Vector2.Dot(vertices[0], axis);
        for (var index = 1; index < vertices.Count; index++)
        {
            var value = Vector2.Dot(vertices[index], axis);
            minimum = Fix64.Min(minimum, value);
            maximum = Fix64.Max(maximum, value);
        }
    }

    private static bool CircleCircle(
        PhysicsShape2D left,
        PhysicsShape2D right,
        out PhysicsManifold2D manifold)
    {
        var delta = right.Center - left.Center;
        var radii = left.Radius + right.Radius;
        var distanceSquared = delta.sqrMagnitude;
        if (distanceSquared > radii * radii)
        {
            manifold = default;
            return false;
        }
        var distance = Mathf.Sqrt(distanceSquared);
        var normal = distance <= Fix64.Epsilon ? Vector2.right : delta / distance;
        var penetration = radii - distance;
        manifold = new PhysicsManifold2D(
            left.Center + normal * (left.Radius - penetration * Fix64.Half),
            normal, penetration);
        return true;
    }

    private static bool CirclePolygon(
        PhysicsShape2D circle,
        PhysicsShape2D polygon,
        out PhysicsManifold2D manifold)
    {
        var bestAxis = Vector2.zero;
        var bestOverlap = Fix64.FromRaw(long.MaxValue);
        for (var index = 0; index < polygon.Vertices.Length; index++)
        {
            var edge = polygon.Vertices[(index + 1) % polygon.Vertices.Length] - polygon.Vertices[index];
            if (!TestAxis(circle, polygon, new Vector2(-edge.y, edge.x),
                    ref bestAxis, ref bestOverlap))
            {
                manifold = default;
                return false;
            }
        }
        var closestVertex = polygon.Vertices[0];
        var closestDistance = (closestVertex - circle.Center).sqrMagnitude;
        for (var index = 1; index < polygon.Vertices.Length; index++)
        {
            var distance = (polygon.Vertices[index] - circle.Center).sqrMagnitude;
            if (distance >= closestDistance) continue;
            closestDistance = distance;
            closestVertex = polygon.Vertices[index];
        }
        if (!TestAxis(circle, polygon, closestVertex - circle.Center,
                ref bestAxis, ref bestOverlap))
        {
            manifold = default;
            return false;
        }
        manifold = new PhysicsManifold2D(
            circle.Center + bestAxis * (circle.Radius - bestOverlap * Fix64.Half),
            bestAxis, bestOverlap);
        return true;
    }

    private static bool PolygonPolygon(
        PhysicsShape2D left,
        PhysicsShape2D right,
        out PhysicsManifold2D manifold)
    {
        var bestAxis = Vector2.zero;
        var bestOverlap = Fix64.FromRaw(long.MaxValue);
        if (!TestPolygonAxes(left, right, left, ref bestAxis, ref bestOverlap) ||
            !TestPolygonAxes(left, right, right, ref bestAxis, ref bestOverlap))
        {
            manifold = default;
            return false;
        }
        manifold = ClipContactEdges(left, right, bestAxis, bestOverlap);
        return true;
    }

    private static bool TestPolygonAxes(PhysicsShape2D left, PhysicsShape2D right,
        PhysicsShape2D source, ref Vector2 bestAxis, ref Fix64 bestOverlap)
    {
        for (var index = 0; index < source.Vertices.Length; index++)
        {
            var edge = source.Vertices[(index + 1) % source.Vertices.Length] - source.Vertices[index];
            if (!TestAxis(left, right, new Vector2(-edge.y, edge.x),
                    ref bestAxis, ref bestOverlap)) return false;
        }
        return true;
    }

    private static bool TestAxis(PhysicsShape2D left, PhysicsShape2D right, Vector2 axis,
        ref Vector2 bestAxis, ref Fix64 bestOverlap)
    {
        if (axis.sqrMagnitude <= Fix64.Epsilon) return true;
        axis = axis.normalized;
        Project(left, axis, out var leftMin, out var leftMax);
        Project(right, axis, out var rightMin, out var rightMax);
        var positive = leftMax - rightMin;
        var negative = rightMax - leftMin;
        if (positive < Fix64.Zero || negative < Fix64.Zero) return false;
        var overlap = Mathf.Min(positive, negative);
        if (overlap >= bestOverlap) return true;
        bestOverlap = overlap;
        bestAxis = positive <= negative ? axis : -axis;
        return true;
    }

    private static void Project(PhysicsShape2D shape, Vector2 axis,
        out Fix64 minimum, out Fix64 maximum)
    {
        if (shape.Type == PhysicsShapeType2D.Circle)
        {
            var center = Vector2.Dot(shape.Center, axis);
            minimum = center - shape.Radius;
            maximum = center + shape.Radius;
            return;
        }
        minimum = maximum = Vector2.Dot(shape.Vertices[0], axis);
        for (var index = 1; index < shape.Vertices.Length; index++)
        {
            var projection = Vector2.Dot(shape.Vertices[index], axis);
            minimum = Mathf.Min(minimum, projection);
            maximum = Mathf.Max(maximum, projection);
        }
    }

    private static Vector2 Support(PhysicsShape2D shape, Vector2 direction)
    {
        if (shape.Type == PhysicsShapeType2D.Circle)
            return shape.Center + direction.normalized * shape.Radius;
        var result = shape.Vertices[0];
        var projection = Vector2.Dot(result, direction);
        for (var index = 1; index < shape.Vertices.Length; index++)
        {
            var candidate = Vector2.Dot(shape.Vertices[index], direction);
            if (candidate <= projection) continue;
            projection = candidate;
            result = shape.Vertices[index];
        }
        return result;
    }

    private static bool RaycastCircle(PhysicsShape2D circle, Vector2 origin, Vector2 direction,
        Fix64 maxDistance, out Fix64 distance, out Vector2 normal)
    {
        var offset = origin - circle.Center;
        var c = offset.sqrMagnitude - circle.Radius * circle.Radius;
        if (c <= Fix64.Zero)
        {
            distance = Fix64.Zero;
            normal = -direction;
            return true;
        }
        var projection = Vector2.Dot(offset, direction);
        var discriminant = projection * projection - c;
        if (projection > Fix64.Zero || discriminant < Fix64.Zero)
        {
            distance = default;
            normal = default;
            return false;
        }
        distance = -projection - Mathf.Sqrt(discriminant);
        normal = (origin + direction * distance - circle.Center).normalized;
        return distance >= Fix64.Zero && distance <= maxDistance;
    }

    private static bool RaySegment(Vector2 origin, Vector2 direction,
        Vector2 from, Vector2 to, out Fix64 rayTime)
    {
        var edge = to - from;
        var denominator = Cross(direction, edge);
        if (Fix64.Abs(denominator) <= Fix64.Epsilon)
        {
            rayTime = default;
            return false;
        }
        var offset = from - origin;
        // Near-parallel rays can intersect the infinite edge far outside Q32.32.
        // Reject out-of-segment and unrepresentable hits before narrowing to Fix64.
        var edgeRaw = ((Int128)Cross(offset, direction).RawValue << Fix64.FractionalBits) / denominator.RawValue;
        rayTime = default;
        if (edgeRaw < 0 || edgeRaw > Fix64.OneRaw) return false;
        var rayRaw = ((Int128)Cross(offset, edge).RawValue << Fix64.FractionalBits) / denominator.RawValue;
        if (rayRaw < 0 || rayRaw > long.MaxValue) return false;
        rayTime = Fix64.FromRaw((long)rayRaw);
        return true;
    }

    private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 from, Vector2 to)
    {
        var segment = to - from;
        if (segment.sqrMagnitude <= Fix64.Epsilon) return from;
        return from + segment * Mathf.Clamp01(Vector2.Dot(point - from, segment) / segment.sqrMagnitude);
    }

    private static void Orient(ref Vector2 axis, Vector2 from, Vector2 to)
    {
        if (Vector2.Dot(to - from, axis) < Fix64.Zero) axis = -axis;
    }

    private static Fix64 Cross(Vector2 left, Vector2 right) =>
        left.x * right.y - left.y * right.x;
}
