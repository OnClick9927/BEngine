using System.Runtime.CompilerServices;
using BEngine.Entities;

namespace BEngine.Physics2D;

public sealed class PhysicsWorld2D : ISceneRuntimeSystem
{
    private static readonly ConditionalWeakTable<Scene, PhysicsWorld2D> Worlds = new();
    private static PhysicsWorld2D? _active;
    private readonly Dictionary<ContactKey, ContactState> _contacts = [];
    private Scene? _scene;

    private static PhysicsWorld2D? ActiveWorld => World.Current?.Scene is { } scene &&
        Worlds.TryGetValue(scene, out var world) ? world : _active;

    public int order => -100;
    public string packageId => "com.bengine.physics2d";

    public void Start(Scene scene)
    {
        _scene = scene;
        Worlds.Remove(scene);
        Worlds.Add(scene, this);
        _active = this;
    }

    public void FixedUpdate(Scene scene, Fix64 fixedDeltaTime)
    {
        _active = this;
        if (Physics2D.autoSimulation) Simulate(fixedDeltaTime);
    }

    public void Stop(Scene scene)
    {
        DispatchExits(_contacts.Values);
        _contacts.Clear();
        Worlds.Remove(scene);
        if (ReferenceEquals(_active, this)) _active = null;
        _scene = null;
    }

    public static PhysicsWorld2D? Get(Scene scene) =>
        Worlds.TryGetValue(scene, out var world) ? world : null;

    public void Simulate(Fix64 deltaTime)
    {
        if (_scene is null || deltaTime <= Fix64.Zero) return;
        foreach (var body in _scene.QueryComponents<Rigidbody2D>().ToArray()
                     .Where(IsActive).OrderBy(item => item.Id))
            Integrate(body, deltaTime);

        var colliders = ActiveColliders(_scene);
        var current = new Dictionary<ContactKey, ContactState>();
        for (var leftIndex = 0; leftIndex < colliders.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < colliders.Length; rightIndex++)
            {
                var left = colliders[leftIndex];
                var right = colliders[rightIndex];
                if (ReferenceEquals(left.gameObject, right.gameObject) ||
                    Physics2D.ShouldIgnore(left, right) ||
                    left.attachedRigidbody?.simulated == false ||
                    right.attachedRigidbody?.simulated == false)
                    continue;
                if (!TryContact(left, right, out var contact)) continue;

                var state = new ContactState(left, right, contact, left.isTrigger || right.isTrigger);
                var key = ContactKey.Create(left, right);
                current[key] = state;
                if (!state.IsTrigger) Resolve(state);
                Dispatch(state, _contacts.ContainsKey(key) ? ContactPhase.Stay : ContactPhase.Enter);
            }
        }

        foreach (var exited in _contacts.Where(pair => !current.ContainsKey(pair.Key)).Select(pair => pair.Value))
            Dispatch(exited, ContactPhase.Exit);
        _contacts.Clear();
        foreach (var pair in current) _contacts[pair.Key] = pair.Value;
    }

    internal static void SimulateActive(Fix64 deltaTime) => ActiveWorld?.Simulate(deltaTime);

    private static bool IsActive(Component component) =>
        component.enabled && component.gameObject.activeInHierarchy;

    private static void Integrate(Rigidbody2D body, Fix64 deltaTime)
    {
        if (body.bodyType != RigidbodyType2D.Dynamic || body.IsSleeping)
        {
            body.ConsumeAcceleration();
            body.ConsumeAngularAcceleration();
            return;
        }

        var previous = body.position;
        var acceleration = body.ConsumeAcceleration() + Physics2D.gravity * body.gravityScale;
        body.linearVelocity += acceleration * deltaTime;
        body.linearVelocity *= Mathf.Clamp01(Fix64.One - body.linearDamping * deltaTime);
        body.linearVelocity = ApplyPositionConstraints(body.linearVelocity, body.constraints);
        body.position += body.linearVelocity * deltaTime;

        body.angularVelocity += body.ConsumeAngularAcceleration() * deltaTime;
        body.angularVelocity *= Mathf.Clamp01(Fix64.One - body.angularDamping * deltaTime);
        if (!body.constraints.HasFlag(RigidbodyConstraints2D.FreezeRotation))
            body.rotation += body.angularVelocity * deltaTime;

        if (body.collisionDetectionMode is not (CollisionDetectionMode.Continuous or
            CollisionDetectionMode.ContinuousDynamic) || ActiveWorld is null)
            return;
        var distance = Vector2.Distance(previous, body.position);
        if (distance <= Fix64.Epsilon || !Raycast(previous, body.linearVelocity.normalized, out var hit,
                distance, ~body.gameObject.layer, QueryTriggerInteraction.Ignore))
            return;
        body.position = hit.point - body.linearVelocity.normalized * Fix64.Parse("0.001");
        body.linearVelocity = Vector2.zero;
    }

    private static Vector2 ApplyPositionConstraints(Vector2 value, RigidbodyConstraints2D constraints) => new(
        constraints.HasFlag(RigidbodyConstraints2D.FreezePositionX) ? Fix64.Zero : value.x,
        constraints.HasFlag(RigidbodyConstraints2D.FreezePositionY) ? Fix64.Zero : value.y);

    private static void Resolve(ContactState state)
    {
        var leftBody = state.Left.attachedRigidbody;
        var rightBody = state.Right.attachedRigidbody;
        var leftInvMass = InverseMass(leftBody);
        var rightInvMass = InverseMass(rightBody);
        var sum = leftInvMass + rightInvMass;
        if (sum <= Fix64.Zero) return;

        var correction = state.Contact.Normal * state.Contact.Penetration;
        if (leftInvMass > 0) leftBody!.position -= correction * (leftInvMass / sum);
        if (rightInvMass > 0) rightBody!.position += correction * (rightInvMass / sum);

        var leftVelocity = leftBody?.linearVelocity ?? Vector2.zero;
        var rightVelocity = rightBody?.linearVelocity ?? Vector2.zero;
        var closing = Vector2.Dot(rightVelocity - leftVelocity, state.Contact.Normal);
        if (closing >= Fix64.Zero) return;
        var bounce = Mathf.Max(state.Left.material?.bounciness ?? Fix64.Zero,
            state.Right.material?.bounciness ?? Fix64.Zero);
        var impulse = -(Fix64.One + bounce) * closing / sum;
        if (leftInvMass > 0) leftBody!.linearVelocity -= state.Contact.Normal * impulse * leftInvMass;
        if (rightInvMass > 0) rightBody!.linearVelocity += state.Contact.Normal * impulse * rightInvMass;
    }

    private static Fix64 InverseMass(Rigidbody2D? body) =>
        body is null || body.bodyType != RigidbodyType2D.Dynamic || body.mass <= Fix64.Zero
            ? Fix64.Zero
            : Fix64.One / body.mass;

    internal static Vector2 GetClosestPoint(Collider2D collider, Vector2 point)
    {
        var bounds = BoundsFor(collider);
        return new Vector2(
            Mathf.Clamp(point.x, bounds.Min.x, bounds.Max.x),
            Mathf.Clamp(point.y, bounds.Min.y, bounds.Max.y));
    }

    internal static Bounds2D GetBounds(Collider2D collider)
    {
        var bounds = BoundsFor(collider);
        return new Bounds2D(bounds.Center, bounds.Max - bounds.Min);
    }

    internal static bool Raycast(Vector2 origin, Vector2 direction, out RaycastHit2D hit,
        Fix64 maxDistance, ulong layerMask, QueryTriggerInteraction triggerInteraction)
    {
        hit = RaycastAll(origin, direction, maxDistance, layerMask, triggerInteraction).FirstOrDefault();
        return hit.collider is not null;
    }

    internal static RaycastHit2D[] RaycastAll(Vector2 origin, Vector2 direction, Fix64 maxDistance,
        ulong layerMask, QueryTriggerInteraction triggerInteraction)
    {
        var active = ActiveWorld;
        if (active?._scene is null || direction.sqrMagnitude <= Fix64.Epsilon) return [];
        direction = direction.normalized;
        var results = new List<RaycastHit2D>();
        foreach (var collider in ActiveColliders(active._scene))
        {
            if (!LayerMatches(collider, layerMask) || !TriggerMatches(collider, triggerInteraction)) continue;
            if (!RayIntersects(BoundsFor(collider), origin, direction, maxDistance,
                    out var distance, out var normal))
                continue;
            results.Add(new RaycastHit2D
            {
                collider = collider,
                distance = distance,
                point = origin + direction * distance,
                normal = normal
            });
        }
        return [.. results.OrderBy(item => item.distance).ThenBy(item => item.collider.Id)];
    }

    internal static Collider2D[] OverlapCircle(Vector2 position, Fix64 radius, ulong layerMask,
        QueryTriggerInteraction triggerInteraction)
    {
        var active = ActiveWorld;
        if (active?._scene is null) return [];
        return [.. ActiveColliders(active._scene).Where(collider => LayerMatches(collider, layerMask) &&
            TriggerMatches(collider, triggerInteraction) &&
            Vector2.Distance(GetClosestPoint(collider, position), position) <= radius)];
    }

    internal static bool CircleCast(Vector2 origin, Fix64 radius, Vector2 direction,
        out RaycastHit2D hit, Fix64 maxDistance, ulong layerMask,
        QueryTriggerInteraction triggerInteraction)
    {
        hit = default;
        var active = ActiveWorld;
        if (active?._scene is null || direction.sqrMagnitude <= Fix64.Epsilon) return false;
        direction = direction.normalized;
        var results = new List<RaycastHit2D>();
        foreach (var collider in ActiveColliders(active._scene))
        {
            if (!LayerMatches(collider, layerMask) || !TriggerMatches(collider, triggerInteraction)) continue;
            var bounds = BoundsFor(collider);
            var padding = Vector2.one * radius;
            var expanded = new BoundsData(bounds.Min - padding, bounds.Max + padding);
            if (!RayIntersects(expanded, origin, direction, maxDistance, out var distance, out var normal))
                continue;
            results.Add(new RaycastHit2D
            {
                collider = collider,
                distance = distance,
                point = origin + direction * distance - normal * radius,
                normal = normal
            });
        }
        if (results.Count == 0) return false;
        hit = results.OrderBy(item => item.distance).ThenBy(item => item.collider.Id).First();
        return true;
    }

    internal static Collider2D[] OverlapBox(Vector2 center, Vector2 size, ulong layerMask,
        QueryTriggerInteraction triggerInteraction)
    {
        var active = ActiveWorld;
        if (active?._scene is null) return [];
        var half = size / 2;
        var bounds = new BoundsData(center - half, center + half);
        return [.. ActiveColliders(active._scene).Where(collider => LayerMatches(collider, layerMask) &&
            TriggerMatches(collider, triggerInteraction) && BoundsFor(collider).Intersects(bounds))];
    }

    internal static void SyncTransforms() { }

    private static Collider2D[] ActiveColliders(Scene scene) =>
        [.. scene.QueryComponents<Collider2D>().ToArray().Where(IsActive).OrderBy(item => item.Id)];

    private static bool LayerMatches(Collider2D collider, ulong mask) =>
        (mask & collider.gameObject.layer) != 0;

    private static bool TriggerMatches(Collider2D collider, QueryTriggerInteraction value) => value switch
    {
        QueryTriggerInteraction.Ignore => !collider.isTrigger,
        QueryTriggerInteraction.Collide => true,
        _ => Physics2D.queriesHitTriggers || !collider.isTrigger
    };

    private static BoundsData BoundsFor(Collider2D collider)
    {
        var center = collider.transform.TransformPoint(collider.offset);
        var scale = collider.transform.lossyScale;
        var absoluteScale = new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        var half = collider switch
        {
            CircleCollider2D circle => Vector2.one * circle.radius *
                Mathf.Max(absoluteScale.x, absoluteScale.y),
            CapsuleCollider2D capsule => Vector2.Scale(capsule.size, absoluteScale) / 2,
            PolygonCollider2D polygon => PolygonHalfExtents(polygon, absoluteScale),
            BoxCollider2D box => Vector2.Scale(box.size, absoluteScale) / 2,
            _ => absoluteScale / 2
        };
        return new BoundsData(center - half, center + half);
    }

    private static Vector2 PolygonHalfExtents(PolygonCollider2D polygon, Vector2 scale)
    {
        if (polygon.points.Count == 0) return scale / 2;
        var minimum = polygon.points.Aggregate(Vector2.Min);
        var maximum = polygon.points.Aggregate(Vector2.Max);
        var size = maximum - minimum;
        return Vector2.Scale(size, scale) / 2;
    }

    private static bool TryContact(Collider2D left, Collider2D right, out ContactData contact)
    {
        var leftBounds = BoundsFor(left);
        var rightBounds = BoundsFor(right);
        if (!leftBounds.Intersects(rightBounds))
        {
            contact = default;
            return false;
        }
        var overlapX = Mathf.Min(leftBounds.Max.x, rightBounds.Max.x) -
                       Mathf.Max(leftBounds.Min.x, rightBounds.Min.x);
        var overlapY = Mathf.Min(leftBounds.Max.y, rightBounds.Max.y) -
                       Mathf.Max(leftBounds.Min.y, rightBounds.Min.y);
        var delta = rightBounds.Center - leftBounds.Center;
        var normal = new Vector2(delta.x >= 0 ? 1 : -1, 0);
        var penetration = overlapX;
        if (overlapY < penetration)
        {
            penetration = overlapY;
            normal = new Vector2(0, delta.y >= 0 ? 1 : -1);
        }
        contact = new ContactData((leftBounds.Center + rightBounds.Center) / 2, normal, penetration);
        return true;
    }

    private static bool RayIntersects(BoundsData bounds, Vector2 origin, Vector2 direction,
        Fix64 maxDistance, out Fix64 distance, out Vector2 normal)
    {
        var minimumTime = Fix64.Zero;
        var maximumTime = maxDistance;
        normal = Vector2.zero;
        for (var axis = 0; axis < 2; axis++)
        {
            var originAxis = axis == 0 ? origin.x : origin.y;
            var directionAxis = axis == 0 ? direction.x : direction.y;
            var minimumAxis = axis == 0 ? bounds.Min.x : bounds.Min.y;
            var maximumAxis = axis == 0 ? bounds.Max.x : bounds.Max.y;
            if (Mathf.Abs(directionAxis) <= Fix64.Epsilon)
            {
                if (originAxis < minimumAxis || originAxis > maximumAxis)
                {
                    distance = default;
                    return false;
                }
                continue;
            }
            var near = (minimumAxis - originAxis) / directionAxis;
            var far = (maximumAxis - originAxis) / directionAxis;
            var sign = -1;
            if (near > far)
            {
                (near, far) = (far, near);
                sign = 1;
            }
            if (near > minimumTime)
            {
                minimumTime = near;
                normal = axis == 0 ? new Vector2(sign, 0) : new Vector2(0, sign);
            }
            maximumTime = Mathf.Min(maximumTime, far);
            if (minimumTime <= maximumTime) continue;
            distance = default;
            return false;
        }
        distance = minimumTime;
        return distance >= Fix64.Zero && distance <= maxDistance;
    }

    private static void Dispatch(ContactState state, ContactPhase phase)
    {
        if (state.IsTrigger)
        {
            Invoke(state.Left.gameObject, $"OnTrigger{phase}2D", state.Right);
            Invoke(state.Right.gameObject, $"OnTrigger{phase}2D", state.Left);
            return;
        }
        Invoke(state.Left.gameObject, $"OnCollision{phase}2D",
            CreateCollision(state.Left, state.Right, state.Contact));
        var reversed = state.Contact with { Normal = -state.Contact.Normal };
        Invoke(state.Right.gameObject, $"OnCollision{phase}2D",
            CreateCollision(state.Right, state.Left, reversed));
    }

    private static void DispatchExits(IEnumerable<ContactState> states)
    {
        foreach (var state in states) Dispatch(state, ContactPhase.Exit);
    }

    private static Collision2D CreateCollision(Collider2D own, Collider2D other,
        ContactData contact) => new()
    {
        collider = other,
        relativeVelocity = (other.attachedRigidbody?.linearVelocity ?? Vector2.zero) -
                           (own.attachedRigidbody?.linearVelocity ?? Vector2.zero),
        contacts =
        [
            new ContactPoint2D
            {
                point = contact.Point,
                normal = contact.Normal,
                separation = -contact.Penetration,
                thisCollider = own,
                otherCollider = other
            }
        ]
    };

    private static void Invoke(GameObject target, string methodName, object argument)
    {
        foreach (var component in target.components)
        {
            if (component is not MonoBehaviour { enabled: true } behaviour) continue;
            try { RuntimeTypeCache.TryInvokeMessage(behaviour, methodName, argument); }
            catch (Exception exception)
            {
                Debug.LogError($"{behaviour.GetType().FullName}.{methodName} failed: {exception.Message}");
            }
        }
    }

    private readonly record struct BoundsData(Vector2 Min, Vector2 Max)
    {
        public Vector2 Center => (Min + Max) / 2;
        public bool Intersects(BoundsData other) =>
            Min.x <= other.Max.x && Max.x >= other.Min.x &&
            Min.y <= other.Max.y && Max.y >= other.Min.y;
    }

    private readonly record struct ContactData(Vector2 Point, Vector2 Normal, Fix64 Penetration);
    private readonly record struct ContactKey(Guid Left, Guid Right)
    {
        public static ContactKey Create(Collider2D left, Collider2D right) =>
            left.Id.CompareTo(right.Id) < 0 ? new(left.Id, right.Id) : new(right.Id, left.Id);
    }
    private sealed record ContactState(
        Collider2D Left, Collider2D Right, ContactData Contact, bool IsTrigger);
    private enum ContactPhase { Enter, Stay, Exit }
}
