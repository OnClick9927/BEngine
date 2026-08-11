using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Physics3D;

public sealed class PhysicsWorld : ISceneRuntimeSystem
{
    private static readonly ConditionalWeakTable<Scene, PhysicsWorld> Worlds = new();
    private static PhysicsWorld? _active;
    private readonly Dictionary<ContactKey, ContactState> _contacts = [];
    private Scene? _scene;

    public int order => -100;
    public string packageId => "com.bengine.physics3d";

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
        if (Physics.autoSimulation) Simulate(fixedDeltaTime);
    }

    public void Stop(Scene scene)
    {
        DispatchExits(_contacts.Values);
        _contacts.Clear();
        Worlds.Remove(scene);
        if (ReferenceEquals(_active, this)) _active = null;
        _scene = null;
    }

    public static PhysicsWorld? Get(Scene scene) => Worlds.TryGetValue(scene, out var world) ? world : null;

    public void Simulate(Fix64 deltaTime)
    {
        if (_scene is null || deltaTime <= Fix64.Zero) return;
        var bodies = _scene.gameObjects.Where(item => item.activeInHierarchy)
            .Select(item => item.GetComponent<Rigidbody>()).Where(item => item is not null).Cast<Rigidbody>()
            .Where(item => item.enabled).OrderBy(item => item.Id).ToArray();
        foreach (var body in bodies) Integrate(body, deltaTime);

        var colliders = ActiveColliders(_scene);
        var current = new Dictionary<ContactKey, ContactState>();
        for (var i = 0; i < colliders.Length; i++)
        {
            for (var j = i + 1; j < colliders.Length; j++)
            {
                var left = colliders[i];
                var right = colliders[j];
                if (ReferenceEquals(left.gameObject, right.gameObject) || Physics.ShouldIgnore(left, right) ||
                    left.attachedRigidbody?.detectCollisions == false ||
                    right.attachedRigidbody?.detectCollisions == false) continue;
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

    internal static void SimulateActive(Fix64 deltaTime) => _active?.Simulate(deltaTime);

    private static void Integrate(Rigidbody body, Fix64 deltaTime)
    {
        if (body.isKinematic || body.IsSleeping) { body.ConsumeAcceleration(); return; }
        var previous = body.position;
        var acceleration = body.ConsumeAcceleration() + (body.useGravity ? Physics.gravity : Vector3.zero);
        body.linearVelocity += acceleration * deltaTime;
        var damping = Mathf.Clamp01(Fix64.One - body.linearDamping * deltaTime);
        body.linearVelocity *= damping;
        var velocity = ApplyPositionConstraints(body.linearVelocity, body.constraints);
        body.linearVelocity = velocity;
        body.position += velocity * deltaTime;

        if (body.collisionDetectionMode is CollisionDetectionMode.Continuous or
            CollisionDetectionMode.ContinuousDynamic && _active is not null)
        {
            var distance = Vector3.Distance(previous, body.position);
            if (distance > Fix64.Epsilon && Raycast(previous, velocity.normalized, out var hit, distance,
                    ~(1 << body.gameObject.layer), QueryTriggerInteraction.Ignore))
            {
                body.position = hit.point - velocity.normalized * Fix64.Parse("0.001");
                body.linearVelocity = Vector3.zero;
            }
        }
    }

    private static Vector3 ApplyPositionConstraints(Vector3 value, RigidbodyConstraints constraints) => new(
        constraints.HasFlag(RigidbodyConstraints.FreezePositionX) ? Fix64.Zero : value.x,
        constraints.HasFlag(RigidbodyConstraints.FreezePositionY) ? Fix64.Zero : value.y,
        constraints.HasFlag(RigidbodyConstraints.FreezePositionZ) ? Fix64.Zero : value.z);

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

        var leftVelocity = leftBody?.linearVelocity ?? Vector3.zero;
        var rightVelocity = rightBody?.linearVelocity ?? Vector3.zero;
        var closing = Vector3.Dot(rightVelocity - leftVelocity, state.Contact.Normal);
        if (closing >= Fix64.Zero) return;
        var bounce = Mathf.Max(state.Left.material?.bounciness ?? Fix64.Zero,
            state.Right.material?.bounciness ?? Fix64.Zero);
        var impulse = -(Fix64.One + bounce) * closing / sum;
        if (leftInvMass > 0) leftBody!.linearVelocity -= state.Contact.Normal * impulse * leftInvMass;
        if (rightInvMass > 0) rightBody!.linearVelocity += state.Contact.Normal * impulse * rightInvMass;
    }

    private static Fix64 InverseMass(Rigidbody? body) =>
        body is null || body.isKinematic || body.mass <= Fix64.Zero ? Fix64.Zero : Fix64.One / body.mass;

    internal static Vector3 GetClosestPoint(Collider collider, Vector3 point)
    {
        var bounds = BoundsFor(collider);
        return new Vector3(
            Mathf.Clamp(point.x, bounds.Min.x, bounds.Max.x),
            Mathf.Clamp(point.y, bounds.Min.y, bounds.Max.y),
            Mathf.Clamp(point.z, bounds.Min.z, bounds.Max.z));
    }

    internal static Bounds GetBounds(Collider collider)
    {
        var bounds = BoundsFor(collider);
        return new Bounds(bounds.Center, bounds.Max - bounds.Min);
    }

    internal static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit,
        Fix64 maxDistance, int layerMask, QueryTriggerInteraction triggerInteraction)
    {
        hit = RaycastAll(origin, direction, maxDistance, layerMask, triggerInteraction).FirstOrDefault();
        return hit.collider is not null;
    }

    internal static RaycastHit[] RaycastAll(Vector3 origin, Vector3 direction, Fix64 maxDistance,
        int layerMask, QueryTriggerInteraction triggerInteraction)
    {
        if (_active?._scene is null || direction.sqrMagnitude <= Fix64.Epsilon) return [];
        direction = direction.normalized;
        var results = new List<RaycastHit>();
        foreach (var collider in ActiveColliders(_active._scene))
        {
            if (!LayerMatches(collider, layerMask) || !TriggerMatches(collider, triggerInteraction)) continue;
            if (RayIntersects(BoundsFor(collider), origin, direction, maxDistance, out var distance, out var normal))
            {
                results.Add(new RaycastHit { collider = collider, distance = distance,
                    point = origin + direction * distance, normal = normal });
            }
        }
        return [.. results.OrderBy(item => item.distance).ThenBy(item => item.collider.Id)];
    }

    internal static Collider[] OverlapSphere(Vector3 position, Fix64 radius, int layerMask,
        QueryTriggerInteraction triggerInteraction)
    {
        if (_active?._scene is null) return [];
        return [.. ActiveColliders(_active._scene).Where(collider => LayerMatches(collider, layerMask) &&
            TriggerMatches(collider, triggerInteraction) &&
            Vector3.Distance(GetClosestPoint(collider, position), position) <= radius)];
    }

    internal static bool SphereCast(Vector3 origin, Fix64 radius, Vector3 direction, out RaycastHit hit,
        Fix64 maxDistance, int layerMask, QueryTriggerInteraction triggerInteraction)
    {
        hit = default;
        if (_active?._scene is null || direction.sqrMagnitude <= Fix64.Epsilon) return false;
        direction = direction.normalized;
        var results = new List<RaycastHit>();
        foreach (var collider in ActiveColliders(_active._scene))
        {
            if (!LayerMatches(collider, layerMask) || !TriggerMatches(collider, triggerInteraction)) continue;
            var bounds = BoundsFor(collider);
            var padding = Vector3.one * radius;
            var expanded = new Bounds3(bounds.Min - padding, bounds.Max + padding);
            if (RayIntersects(expanded, origin, direction, maxDistance, out var distance, out var normal))
                results.Add(new RaycastHit { collider = collider, distance = distance,
                    point = origin + direction * distance - normal * radius, normal = normal });
        }
        if (results.Count == 0) return false;
        hit = results.OrderBy(item => item.distance).ThenBy(item => item.collider.Id).First();
        return true;
    }

    internal static Collider[] OverlapBox(Vector3 center, Vector3 halfExtents, int layerMask,
        QueryTriggerInteraction triggerInteraction)
    {
        if (_active?._scene is null) return [];
        var bounds = new Bounds3(center - halfExtents, center + halfExtents);
        return [.. ActiveColliders(_active._scene).Where(collider => LayerMatches(collider, layerMask) &&
            TriggerMatches(collider, triggerInteraction) && BoundsFor(collider).Intersects(bounds))];
    }

    internal static void SyncTransforms() { }

    private static Collider[] ActiveColliders(Scene scene) => scene.gameObjects
        .Where(item => item.activeInHierarchy)
        .SelectMany(item => item.GetComponents<Collider>()).Where(item => item.enabled)
        .OrderBy(item => item.Id).ToArray();

    private static bool LayerMatches(Collider collider, int mask) => (mask & (1 << collider.gameObject.layer)) != 0;
    private static bool TriggerMatches(Collider collider, QueryTriggerInteraction value) => value switch
    {
        QueryTriggerInteraction.Ignore => !collider.isTrigger,
        QueryTriggerInteraction.Collide => true,
        _ => Physics.queriesHitTriggers || !collider.isTrigger
    };

    private static Bounds3 BoundsFor(Collider collider)
    {
        if (collider is ITerrainCollisionSource terrain)
            return new Bounds3(terrain.boundsMin, terrain.boundsMax);
        var center = collider.transform.TransformPoint(collider.center);
        var scale = collider.transform.lossyScale;
        var absScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        var half = collider switch
        {
            SphereCollider sphere => Vector3.one * sphere.radius * Mathf.Max(absScale.x,
                Mathf.Max(absScale.y, absScale.z)),
            CapsuleCollider capsule when capsule.direction == 0 => new Vector3(capsule.height / 2,
                capsule.radius, capsule.radius),
            CapsuleCollider capsule when capsule.direction == 2 => new Vector3(capsule.radius,
                capsule.radius, capsule.height / 2),
            CapsuleCollider capsule => new Vector3(capsule.radius, capsule.height / 2, capsule.radius),
            MeshCollider mesh => Vector3.Scale(mesh.boundsSize, absScale) / 2,
            BoxCollider box => Vector3.Scale(box.size, absScale) / 2,
            _ => absScale / 2
        };
        return new Bounds3(center - half, center + half);
    }

    private static bool TryContact(Collider left, Collider right, out ContactData contact)
    {
        if (left is ITerrainCollisionSource leftTerrain)
            return TryTerrainContact(leftTerrain, right, terrainIsLeft: true, out contact);
        if (right is ITerrainCollisionSource rightTerrain)
            return TryTerrainContact(rightTerrain, left, terrainIsLeft: false, out contact);
        var a = BoundsFor(left);
        var b = BoundsFor(right);
        if (!a.Intersects(b)) { contact = default; return false; }
        var overlapX = Mathf.Min(a.Max.x, b.Max.x) - Mathf.Max(a.Min.x, b.Min.x);
        var overlapY = Mathf.Min(a.Max.y, b.Max.y) - Mathf.Max(a.Min.y, b.Min.y);
        var overlapZ = Mathf.Min(a.Max.z, b.Max.z) - Mathf.Max(a.Min.z, b.Min.z);
        var delta = b.Center - a.Center;
        var penetration = overlapX;
        var normal = new Vector3(delta.x >= 0 ? 1 : -1, 0, 0);
        if (overlapY < penetration) { penetration = overlapY; normal = new Vector3(0, delta.y >= 0 ? 1 : -1, 0); }
        if (overlapZ < penetration) { penetration = overlapZ; normal = new Vector3(0, 0, delta.z >= 0 ? 1 : -1); }
        contact = new ContactData((a.Center + b.Center) / 2, normal, penetration);
        return true;
    }

    private static bool TryTerrainContact(ITerrainCollisionSource terrain, Collider other,
        bool terrainIsLeft, out ContactData contact)
    {
        var bounds = BoundsFor(other);
        if (!terrain.SampleSurface(bounds.Center, out var height, out var normal) || bounds.Min.y > height)
        {
            contact = default;
            return false;
        }
        var penetration = height - bounds.Min.y;
        contact = new ContactData(new Vector3(bounds.Center.x, height, bounds.Center.z),
            terrainIsLeft ? normal : -normal, penetration);
        return true;
    }

    private static bool RayIntersects(Bounds3 bounds, Vector3 origin, Vector3 direction,
        Fix64 maxDistance, out Fix64 distance, out Vector3 normal)
    {
        var tMin = Fix64.Zero;
        var tMax = maxDistance;
        normal = Vector3.zero;
        foreach (var axis in new[] { 0, 1, 2 })
        {
            var o = axis == 0 ? origin.x : axis == 1 ? origin.y : origin.z;
            var d = axis == 0 ? direction.x : axis == 1 ? direction.y : direction.z;
            var min = axis == 0 ? bounds.Min.x : axis == 1 ? bounds.Min.y : bounds.Min.z;
            var max = axis == 0 ? bounds.Max.x : axis == 1 ? bounds.Max.y : bounds.Max.z;
            if (Mathf.Abs(d) <= Fix64.Epsilon)
            {
                if (o < min || o > max) { distance = default; return false; }
                continue;
            }
            var near = (min - o) / d;
            var far = (max - o) / d;
            var sign = -1;
            if (near > far) { (near, far) = (far, near); sign = 1; }
            if (near > tMin)
            {
                tMin = near;
                normal = axis == 0 ? new Vector3(sign, 0, 0) : axis == 1
                    ? new Vector3(0, sign, 0) : new Vector3(0, 0, sign);
            }
            tMax = Mathf.Min(tMax, far);
            if (tMin > tMax) { distance = default; return false; }
        }
        distance = tMin;
        return distance >= Fix64.Zero && distance <= maxDistance;
    }

    private static void Dispatch(ContactState state, ContactPhase phase)
    {
        if (state.IsTrigger)
        {
            Invoke(state.Left.gameObject, $"OnTrigger{phase}", state.Right);
            Invoke(state.Right.gameObject, $"OnTrigger{phase}", state.Left);
            return;
        }
        Invoke(state.Left.gameObject, $"OnCollision{phase}", CreateCollision(state.Left, state.Right, state.Contact));
        var reversed = state.Contact with { Normal = -state.Contact.Normal };
        Invoke(state.Right.gameObject, $"OnCollision{phase}", CreateCollision(state.Right, state.Left, reversed));
    }

    private static void DispatchExits(IEnumerable<ContactState> states)
    {
        foreach (var state in states) Dispatch(state, ContactPhase.Exit);
    }

    private static Collision CreateCollision(Collider own, Collider other, ContactData contact) => new()
    {
        collider = other,
        relativeVelocity = (other.attachedRigidbody?.linearVelocity ?? Vector3.zero) -
            (own.attachedRigidbody?.linearVelocity ?? Vector3.zero),
        contacts = [new ContactPoint { point = contact.Point, normal = contact.Normal,
            separation = -contact.Penetration, thisCollider = own, otherCollider = other }]
    };

    private static void Invoke(GameObject target, string methodName, object argument)
    {
        foreach (var behaviour in target.GetComponents<MonoBehaviour>().Where(item => item.enabled))
        {
            var method = behaviour.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(candidate => candidate.Name == methodName &&
                    candidate.GetParameters() is [{ ParameterType: var parameterType }] &&
                    parameterType.IsInstanceOfType(argument));
            method?.Invoke(behaviour, [argument]);
        }
    }

    private readonly record struct Bounds3(Vector3 Min, Vector3 Max)
    {
        public Vector3 Center => (Min + Max) / 2;
        public bool Intersects(Bounds3 other) => Min.x <= other.Max.x && Max.x >= other.Min.x &&
            Min.y <= other.Max.y && Max.y >= other.Min.y && Min.z <= other.Max.z && Max.z >= other.Min.z;
    }
    private readonly record struct ContactData(Vector3 Point, Vector3 Normal, Fix64 Penetration);
    private readonly record struct ContactKey(Guid Left, Guid Right)
    {
        public static ContactKey Create(Collider left, Collider right) =>
            left.Id.CompareTo(right.Id) < 0 ? new(left.Id, right.Id) : new(right.Id, left.Id);
    }
    private sealed record ContactState(Collider Left, Collider Right, ContactData Contact, bool IsTrigger);
    private enum ContactPhase { Enter, Stay, Exit }
}
