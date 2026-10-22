namespace BEngine.Physics2D;

public sealed partial class PhysicsWorld2D
{
    private struct SolverPoint(Vector2 position)
    {
        internal readonly Vector2 Position = position;
        internal Fix64 NormalImpulse, TangentImpulse, BounceVelocity;
        internal Fix64 NormalMass, TangentMass;
    }
    private static Fix64 InverseMass(Rigidbody2D? body) =>
        body is { enabled: true, simulated: true, bodyType: RigidbodyType2D.Dynamic } ? Fix64.One / body.mass : Fix64.Zero;
    private static Fix64 InverseInertia(Rigidbody2D? body) =>
        InverseMass(body) > 0 && !body!.constraints.HasFlag(RigidbodyConstraints2D.FreezeRotation) ? Fix64.One / body.inertia : Fix64.Zero;
    private static Fix64 Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    private static Fix64 EffectiveMass(Rigidbody2D? body, Vector2 point, Vector2 direction)
    {
        var inv = InverseMass(body); if (inv <= 0) return 0;
        var arm = Cross(point - body!.worldCenterOfMass, direction);
        return Vector2.Dot(body.Constrain(direction), direction) * inv + arm * arm * InverseInertia(body);
    }
    private static void ApplyPair(ContactState state, Vector2 position, Vector2 impulse)
    {
        ApplyCached(state.LeftBody, state.LeftCenter, state.LeftInverseMass, state.LeftInverseInertia, position, -impulse);
        ApplyCached(state.RightBody, state.RightCenter, state.RightInverseMass, state.RightInverseInertia, position, impulse);
    }
    private static void ApplyCached(Rigidbody2D? body, Vector2 center, Fix64 inverseMass, Fix64 inverseInertia, Vector2 point, Vector2 impulse)
    {
        if (inverseMass <= 0) return;
        body!.linearVelocity += body.Constrain(impulse * inverseMass);
        body.angularVelocity += Cross(point - center, impulse) * inverseInertia * Mathf.Rad2Deg;
        if (impulse.sqrMagnitude > Fix64.Epsilon) body.WakeUp();
    }
    private static Vector2 CachedVelocity(Rigidbody2D? body, Vector2 center, Vector2 point)
    {
        if (body is not { simulated: true, bodyType: not RigidbodyType2D.Static }) return Vector2.zero;
        var arm = point - center;
        return body.linearVelocity + new Vector2(-arm.y, arm.x) * (body.angularVelocity * Mathf.Deg2Rad);
    }
    private void PrepareContact(ContactState state, ContactState? previous, Fix64 deltaTime)
    {
        state.DeltaTime = deltaTime;
        state.LeftBody = state.Left.attachedRigidbody; state.RightBody = state.Right.attachedRigidbody;
        state.LeftInverseMass = InverseMass(state.LeftBody); state.RightInverseMass = InverseMass(state.RightBody);
        state.LeftInverseInertia = InverseInertia(state.LeftBody); state.RightInverseInertia = InverseInertia(state.RightBody);
        state.LeftCenter = state.LeftBody is { bodyType: not RigidbodyType2D.Static } ? state.LeftBody.worldCenterOfMass : Vector2.zero;
        state.RightCenter = state.RightBody is { bodyType: not RigidbodyType2D.Static } ? state.RightBody.worldCenterOfMass : Vector2.zero;
        state.Friction = Fix64.Sqrt(Fix64.Max(0, state.Left.sharedMaterial?.friction ?? (Fix64).4) * Fix64.Max(0, state.Right.sharedMaterial?.friction ?? (Fix64).4));
        state.PointStart = _solverPoints.Count; state.PointCount = state.Manifold.HasSecondPoint ? 2 : 1;
        var restitution = Mathf.Clamp01(Mathf.Max(state.Left.sharedMaterial?.bounciness ?? 0, state.Right.sharedMaterial?.bounciness ?? 0));
        var matched = 0; var normal = state.Manifold.Normal; var tangent = new Vector2(-normal.y, normal.x);
        for (var i = 0; i < state.PointCount; i++)
        {
            var point = new SolverPoint(i == 0 ? state.Manifold.Point : state.Manifold.Point2);
            point.NormalMass = EffectiveMass(state.LeftBody, point.Position, normal) + EffectiveMass(state.RightBody, point.Position, normal);
            point.TangentMass = EffectiveMass(state.LeftBody, point.Position, tangent) + EffectiveMass(state.RightBody, point.Position, tangent);
            var speed = Vector2.Dot(CachedVelocity(state.RightBody, state.RightCenter, point.Position) - CachedVelocity(state.LeftBody, state.LeftCenter, point.Position), normal);
            point.BounceVelocity = speed < -(Fix64).5 ? -restitution * speed : Fix64.Zero;
            if (previous is not null && !previous.IsTrigger && previous.DeltaTime > 0 && Vector2.Dot(previous.Manifold.Normal, normal) >= (Fix64).99)
            {
                var nearest = -1; var distance = Fix64.FromRaw(long.MaxValue);
                for (var j = 0; j < previous.PointCount; j++)
                {
                    if ((matched & (1 << j)) != 0) continue;
                    var candidateDistance = (_previousSolverPoints[previous.PointStart + j].Position - point.Position).sqrMagnitude;
                    if (nearest < 0 || candidateDistance < distance) { nearest = j; distance = candidateDistance; }
                }
                if (nearest >= 0 && distance <= (Fix64).0025)
                {
                    matched |= 1 << nearest;
                    ref readonly var old = ref _previousSolverPoints[previous.PointStart + nearest];
                    var ratio = Mathf.Clamp(deltaTime / previous.DeltaTime, 0, 2);
                    point.NormalImpulse = old.NormalImpulse * ratio; point.TangentImpulse = old.TangentImpulse * ratio;
                }
            }
            _solverPoints.Add(point);
        }
        if (state.PointCount == 2)
        {
            var p = _solverPoints[state.PointStart]; var q = _solverPoints[state.PointStart + 1];
            state.K11 = p.NormalMass; state.K22 = q.NormalMass;
            Fix64 Coupling(Rigidbody2D? body, Vector2 center, Fix64 inverseMass, Fix64 inverseInertia)
            {
                if (inverseMass <= 0) return 0;
                return Vector2.Dot(body!.Constrain(normal), normal) * inverseMass + inverseInertia *
                    Cross(p.Position - center, normal) * Cross(q.Position - center, normal);
            }
            state.K12 = Coupling(state.LeftBody, state.LeftCenter, state.LeftInverseMass, state.LeftInverseInertia) +
                Coupling(state.RightBody, state.RightCenter, state.RightInverseMass, state.RightInverseInertia);
            state.Determinant = state.K11 * state.K22 - state.K12 * state.K12;
        }
    }
    private void WarmStart(ContactState state)
    {
        var normal = state.Manifold.Normal; var tangent = new Vector2(-normal.y, normal.x);
        for (var i = 0; i < state.PointCount; i++)
        { ref readonly var point = ref _solverPoints[state.PointStart + i]; ApplyPair(state, point.Position, normal * point.NormalImpulse + tangent * point.TangentImpulse); }
    }
    private void ResolveVelocity(ContactState state)
    {
        var left = state.LeftBody; var right = state.RightBody;
        var normal = state.Manifold.Normal; var tangent = new Vector2(-normal.y, normal.x);
        var blockSolved = state.PointCount == 2 && SolveNormalBlock(state);
        for (var i = 0; i < state.PointCount; i++)
        {
            ref var point = ref _solverPoints[state.PointStart + i];
            if (!blockSolved)
            {
                var inverse = point.NormalMass;
                if (inverse <= Fix64.Epsilon) continue;
                var speed = Vector2.Dot(CachedVelocity(right, state.RightCenter, point.Position) - CachedVelocity(left, state.LeftCenter, point.Position), normal);
                var next = Fix64.Max(0, point.NormalImpulse + (point.BounceVelocity - speed) / inverse);
                ApplyPair(state, point.Position, normal * (next - point.NormalImpulse)); point.NormalImpulse = next;
            }
            var tangentMass = point.TangentMass;
            if (tangentMass <= Fix64.Epsilon) continue;
            var friction = state.Friction;
            var tangentSpeed = Vector2.Dot(CachedVelocity(right, state.RightCenter, point.Position) - CachedVelocity(left, state.LeftCenter, point.Position), tangent);
            var nextTangent = Mathf.Clamp(point.TangentImpulse - tangentSpeed / tangentMass, -friction * point.NormalImpulse, friction * point.NormalImpulse);
            ApplyPair(state, point.Position, tangent * (nextTangent - point.TangentImpulse)); point.TangentImpulse = nextTangent;
        }
    }
    private bool SolveNormalBlock(ContactState state)
    {
        var left = state.LeftBody; var right = state.RightBody;
        ref var p = ref _solverPoints[state.PointStart]; ref var q = ref _solverPoints[state.PointStart + 1]; var n = state.Manifold.Normal;
        var k11 = state.K11; var k22 = state.K22; var k12 = state.K12; var determinant = state.Determinant;
        if (determinant <= (Fix64).000001 || k11 <= Fix64.Epsilon || k22 <= Fix64.Epsilon) return false;
        var b1 = Vector2.Dot(CachedVelocity(right, state.RightCenter, p.Position) - CachedVelocity(left, state.LeftCenter, p.Position), n) - p.BounceVelocity - k11 * p.NormalImpulse - k12 * q.NormalImpulse;
        var b2 = Vector2.Dot(CachedVelocity(right, state.RightCenter, q.Position) - CachedVelocity(left, state.LeftCenter, q.Position), n) - q.BounceVelocity - k12 * p.NormalImpulse - k22 * q.NormalImpulse;
        var x1 = (k12 * b2 - k22 * b1) / determinant; var x2 = (k12 * b1 - k11 * b2) / determinant;
        if (x1 < 0 || x2 < 0)
        {
            x1 = 0; x2 = -b2 / k22;
            if (x2 < 0 || k12 * x2 + b1 < 0)
            {
                x2 = 0; x1 = -b1 / k11;
                if (x1 < 0 || k12 * x1 + b2 < 0)
                { if (b1 < 0 || b2 < 0) return false; x1 = 0; x2 = 0; }
            }
        }
        ApplyPair(state, p.Position, n * (x1 - p.NormalImpulse)); ApplyPair(state, q.Position, n * (x2 - q.NormalImpulse));
        p.NormalImpulse = x1; q.NormalImpulse = x2; return true;
    }
    private static void ResolvePosition(ContactState state)
    {
        if (!TryLeaf(PhysicsShapeFactory2D.Create(state.Left), state.LeftPart, out var leftShape) ||
            !TryLeaf(PhysicsShapeFactory2D.Create(state.Right), state.RightPart, out var rightShape) ||
            !CollisionAlgorithms2D.TryCollide(leftShape, rightShape, out var manifold)) return;
        var left = state.Left.attachedRigidbody; var right = state.Right.attachedRigidbody;
        var pointCount = manifold.HasSecondPoint ? 2 : 1;
        for (var i = 0; i < pointCount; i++)
        {
            var point = i == 0 ? manifold.Point : manifold.Point2;
            var inverse = EffectiveMass(left, point, manifold.Normal) + EffectiveMass(right, point, manifold.Normal);
            if (inverse <= Fix64.Epsilon) continue;
            var impulse = manifold.Normal * (Mathf.Clamp(manifold.Penetration - (Fix64).001, 0, (Fix64).2) * (Fix64).8 / (inverse * pointCount));
            CorrectBody(left, point, -impulse); CorrectBody(right, point, impulse);
        }
    }
    private static bool TryLeaf(PhysicsShape2D shape, int index, out PhysicsShape2D leaf)
    {
        if (shape.Type != PhysicsShapeType2D.Compound) { leaf = shape; return index == 0; }
        var parts = CollisionAlgorithms2D.Leaves(shape);
        if ((uint)index < (uint)parts.Length) { leaf = parts[index]; return true; }
        leaf = shape; return false;
    }
    private static void CorrectBody(Rigidbody2D? body, Vector2 point, Vector2 impulse)
    {
        var inv = InverseMass(body); if (inv <= 0) return;
        var center = body!.worldCenterOfMass;
        body.rotation += Cross(point - center, impulse) * InverseInertia(body) * Mathf.Rad2Deg;
        body.position += center + body.Constrain(impulse * inv) - body.worldCenterOfMass;
    }
}
