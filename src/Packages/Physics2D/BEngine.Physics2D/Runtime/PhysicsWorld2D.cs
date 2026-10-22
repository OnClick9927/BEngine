using System.Runtime.CompilerServices;

namespace BEngine.Physics2D;

public sealed partial class PhysicsWorld2D : ISceneRuntimeSystem
{
    private static readonly Fix64 CollisionSeparationEpsilon = Fix64.Parse("0.001");
    private static readonly ConditionalWeakTable<Scene, PhysicsWorld2D> Worlds = new();
    private static readonly List<PhysicsWorld2D> RunningWorlds = [];
    private static PhysicsWorld2D? _active;
    private static PhysicsWorld2D? _queryOverride;
    private Dictionary<ContactKey, ContactState> _contacts = [], _currentContacts = [];
    private readonly List<Rigidbody2D> _bodyBatch = [];
    private readonly List<Joint2D> _jointBatch = [];
    private readonly List<Collider2D> _colliderBatch = [];
    private readonly List<Effector2D> _effectorBatch = [];
    private readonly List<ContactState> _solidBatch = [];
    // Contact manifolds currently contain one or two points. Reuse exact-size
    // groups after callbacks finish so the per-pair hot path does not allocate
    // a new reference array every simulation step.
    private readonly Stack<ContactState[]> _singleContactGroups = [];
    private readonly Stack<ContactState[]> _doubleContactGroups = [];
    private readonly Stack<ContactState> _contactStatePool = [];
    private readonly HashSet<(Guid, Guid)> _blockedBodies = [];
    private readonly FrameDataBuffer<ComponentSortEntry> _sortBatch = new();
    private FrameDataBuffer<SolverPoint> _solverPoints = new(), _previousSolverPoints = new();
    private bool _simulating;
    private readonly record struct ComponentSortEntry(Component Component, Guid Id, int Order);
    private static readonly Comparison<ComponentSortEntry> ComponentComparison = CompareComponents;
    private readonly PhysicsWorld2DState _state = new();
    private readonly PhysicsBroadphase2D _broadphase = new();
    private readonly CollisionAlgorithms2D.ContactCollector _contactCollector = new();
    private readonly Dictionary<Collider2D, PhysicsShape2D> _shapeScratch = new(ReferenceEqualityComparer.Instance);
    public int broadphaseCandidateCount { get; private set; }
    public int narrowphaseTestCount { get; private set; }
    public long possiblePairCount { get; private set; }
    static PhysicsWorld2D() => Diagnostics.RuntimeDiagnosticRegistry.Register("budget.physics2d", "Physics 2D broadphase",
        new("possible", "All possible pairs", Diagnostics.RuntimeDiagnosticUnit.Number, () => ActiveWorld?.possiblePairCount ?? 0),
        new("candidates", "AABB candidate pairs", Diagnostics.RuntimeDiagnosticUnit.Number, () => ActiveWorld?.broadphaseCandidateCount ?? 0),
        new("narrow", "Narrowphase tests", Diagnostics.RuntimeDiagnosticUnit.Number, () => ActiveWorld?.narrowphaseTestCount ?? 0),
        new("contacts", "Active contacts", Diagnostics.RuntimeDiagnosticUnit.Number, () => ActiveWorld?._contacts.Count ?? 0));
    private Scene? _scene;

    private static PhysicsWorld2D? ActiveWorld
    {
        get
        {
            if (_queryOverride?._scene is not null) return _queryOverride;
            if (SceneRuntime.currentScene is not { } scene) return _active;
            return Worlds.TryGetValue(scene, out var world) ? world : null;
        }
    }

    internal static PhysicsWorld2DState? activeState => ActiveWorld?._state;
    public int order => -100;
    public string packageId => "com.bengine.physics2d";

    /// <summary>Queries an authored scene without simulating it. Restores the prior query world on disposal.</summary>
    public static IDisposable OpenQueryScope(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var previous = _active;
        var previousOverride = _queryOverride;
        var created = !Worlds.TryGetValue(scene, out var world);
        if (created) { world = new PhysicsWorld2D(); world.Start(scene); }
        _queryOverride = world;
        return new QueryScope(world!, scene, previous, previousOverride, created);
    }

    private sealed class QueryScope(PhysicsWorld2D world, Scene scene, PhysicsWorld2D? previous, PhysicsWorld2D? previousOverride, bool created) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (created) world.Stop(scene);
            _queryOverride = previousOverride?._scene is not null ? previousOverride : null;
            _active = previous?._scene is not null ? previous : RunningWorlds.LastOrDefault();
        }
    }

    public void Start(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_scene is not null && !ReferenceEquals(_scene, scene))
            throw new InvalidOperationException("A PhysicsWorld2D instance cannot run more than one Scene.");
        PhysicsCompilationWarmup.Prepare();
        _state.CopySettingsFrom(Physics2D.defaultState);
        _scene = scene;
        Worlds.Remove(scene);
        Worlds.Add(scene, this);
        Activate();
    }

    public void FixedUpdate(Scene scene, Fix64 fixedDeltaTime)
    {
        Activate();
        if (_state.AutoSimulation) Simulate(fixedDeltaTime);
    }

    public void Stop(Scene scene)
    {
        DispatchExits(_contacts.Values);
        ReleaseContactGroups(_contacts);
        ReleaseContactGroups(_currentContacts);
        _previousSolverPoints.Clear();
        if (!_simulating)
        {
            _currentContacts.Clear(); _solidBatch.Clear(); _solverPoints.Clear();
            _bodyBatch.Clear(); _jointBatch.Clear(); _colliderBatch.Clear(); _effectorBatch.Clear(); _sortBatch.Clear(); _blockedBodies.Clear();
        }
        _shapeScratch.Clear();
        if (!_simulating) _contactCollector.Clear();
        _state.ClearIgnoredPairs();
        Worlds.Remove(scene);
        RunningWorlds.Remove(this);
        if (ReferenceEquals(_active, this)) _active = RunningWorlds.LastOrDefault();
        _scene = null;
    }

    public static PhysicsWorld2D? Get(Scene scene) =>
        Worlds.TryGetValue(scene, out var world) ? world : null;

    internal static PhysicsWorld2DState StateFor(Collider2D left, Collider2D right)
    {
        if (SceneRuntime.currentScene is { } currentScene)
            return Worlds.TryGetValue(currentScene, out var currentWorld)
                ? currentWorld._state
                : Physics2D.defaultState;
        var leftScene = left.gameObject.scene;
        if (leftScene is not null && ReferenceEquals(leftScene, right.gameObject.scene) &&
            Worlds.TryGetValue(leftScene, out var world)) return world._state;
        return activeState ?? Physics2D.defaultState;
    }

    public void Simulate(Fix64 deltaTime)
    {
        if (_scene is null || deltaTime <= Fix64.Zero) return;
        if (_simulating) throw new InvalidOperationException("A PhysicsWorld2D simulation cannot be re-entered.");
        _simulating = true;
        var bodies = _bodyBatch;
        try
        {
            _scene.GetComponents(bodies); FilterActive(bodies); SortById(bodies);
            foreach (var body in bodies) body.BeginMassStep();
            SimulateStep(deltaTime, bodies);
        }
        finally
        {
            foreach (var body in bodies) body.EndMassStep();
            _bodyBatch.Clear(); _jointBatch.Clear(); _colliderBatch.Clear(); _effectorBatch.Clear(); _sortBatch.Clear();
            _solidBatch.Clear(); ReleaseContactGroups(_currentContacts); _blockedBodies.Clear(); _solverPoints.Clear(); _shapeScratch.Clear(); _contactCollector.Clear(); _simulating = false;
        }
    }

    private void SimulateStep(Fix64 deltaTime, List<Rigidbody2D> bodies)
    {
        Activate();
        var joints = _jointBatch; _scene!.GetComponents(joints);
        var jointCount = 0; foreach (var joint in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(joints)) if (joint.canSolve) joints[jointCount++] = joint;
        joints.RemoveRange(jointCount, joints.Count - jointCount); SortById(joints);
        foreach (var joint in joints) joint.Prepare(deltaTime);
        ApplyEffectors();
        foreach (var body in bodies)
            Integrate(body, deltaTime);

        var colliders = _colliderBatch; FillActiveColliders(colliders);
        var shapes = _shapeScratch; shapes.Clear();
        foreach (var collider in colliders) shapes.Add(collider, PhysicsShapeFactory2D.Create(collider));
        var candidates = _broadphase.Calculate(colliders, shapes);
        possiblePairCount = (long)colliders.Count * (colliders.Count - 1) / 2;
        broadphaseCandidateCount = candidates.Count; narrowphaseTestCount = 0;
        var blockedBodies = _blockedBodies; blockedBodies.Clear();
        foreach (var joint in joints)
            if (!joint.enableCollision && joint.connectedBody is { } connected && joint.GetComponent<Rigidbody2D>() is { } owner)
                blockedBodies.Add(owner.Id.CompareTo(connected.Id) < 0 ? (owner.Id, connected.Id) : (connected.Id, owner.Id));
        var current = _currentContacts; current.Clear(); _solverPoints.Clear();
        foreach (var (leftIndex, rightIndex) in candidates)
        {
                var left = colliders[leftIndex];
                var right = colliders[rightIndex];
                var leftBody = left.attachedRigidbody;
                var rightBody = right.attachedRigidbody;
                if (ReferenceEquals(left.gameObject, right.gameObject) ||
                    leftBody is not null && ReferenceEquals(leftBody, rightBody) ||
                    !_state.ShouldCollide(left, right) ||
                    leftBody is not null && rightBody is not null && blockedBodies.Contains(leftBody.Id.CompareTo(rightBody.Id) < 0 ?
                        (leftBody.Id, rightBody.Id) : (rightBody.Id, leftBody.Id)) ||
                    leftBody?.simulated == false || rightBody?.simulated == false)
                    continue;
                narrowphaseTestCount++;
                var contacts = _contactCollector.Collect(shapes[left], shapes[right]);
                if (contacts.Count == 0) continue;
                var manifold = contacts[0].Manifold;
                if (left.usedByEffector && left.GetComponent<PlatformEffector2D>() is { } leftPlatform && !leftPlatform.Allows(manifold.Normal, right) ||
                    right.usedByEffector && right.GetComponent<PlatformEffector2D>() is { } rightPlatform && !rightPlatform.Allows(-manifold.Normal, left)) continue;
                var states = RentContactGroup(contacts.Count);
                for (var i = 0; i < states.Length; i++)
                {
                    var contact = contacts[i];
                    states[i] = RentContactState(left, right, contact.Manifold,
                        left.isTrigger || right.isTrigger, contact.LeftPart, contact.RightPart);
                }
                states[0].Group = states;
                current[ContactKey.Create(left, right)] = states[0];
        }

        var solidContacts = _solidBatch; solidContacts.Clear();
        foreach (var state in current.Values) if (!state.IsTrigger) solidContacts.AddRange(state.Group);
        foreach (var contact in solidContacts)
        {
            _contacts.TryGetValue(ContactKey.Create(contact.Left, contact.Right), out var previous);
            ContactState? previousPart = null;
            if (previous is not null) foreach (var old in previous.Group)
                if (old.LeftPart == contact.LeftPart && old.RightPart == contact.RightPart) { previousPart = old; break; }
            PrepareContact(contact, previousPart, deltaTime);
        }
        foreach (var contact in solidContacts) WarmStart(contact);
        for (var iteration = 0; iteration < Math.Clamp(_state.VelocityIterations, 1, 32); iteration++)
            foreach (var contact in solidContacts) ResolveVelocity(contact);
        for (var iteration = 0; iteration < Math.Clamp(_state.PositionIterations, 1, 32); iteration++)
        {
            foreach (var contact in solidContacts) ResolvePosition(contact);
            foreach (var joint in joints) if (joint.canSolve) joint.Solve(deltaTime);
        }

        foreach (var body in bodies) body.EndMassStep();
        foreach (var (key, state) in current)
            Dispatch(state, _contacts.ContainsKey(key) ? ContactPhase.Stay : ContactPhase.Enter);
        foreach (var pair in _contacts) if (!current.ContainsKey(pair.Key)) Dispatch(pair.Value, ContactPhase.Exit);
        if (_scene is null) return;
        (_contacts, _currentContacts) = (_currentContacts, _contacts);
        (_previousSolverPoints, _solverPoints) = (_solverPoints, _previousSolverPoints);
    }

    internal static void SimulateActive(Fix64 deltaTime) => ActiveWorld?.Simulate(deltaTime);

    private void ApplyEffectors()
    {
        if (_scene is null) return;
        var colliders = _colliderBatch; FillActiveColliders(colliders);
        _scene.GetComponents(_effectorBatch); FilterActive(_effectorBatch); SortById(_effectorBatch);
        foreach (var effector in _effectorBatch)
        {
            if (effector is PlatformEffector2D) continue;
            var region = effector.GetComponent<Collider2D>();
            if (region is not { enabled: true, usedByEffector: true }) continue;
            var shape = PhysicsShapeFactory2D.Create(region);
            var affected = new HashSet<Rigidbody2D>(ReferenceEqualityComparer.Instance);
            foreach (var collider in colliders)
            {
                var body = collider.attachedRigidbody;
                if (body is not { simulated: true, bodyType: RigidbodyType2D.Dynamic } || ReferenceEquals(body, region.attachedRigidbody) ||
                    !effector.Affects(collider) || !_state.ShouldCollide(region, collider) ||
                    !CollisionAlgorithms2D.TryCollide(shape, PhysicsShapeFactory2D.Create(collider), out _) || !affected.Add(body)) continue;
                if (effector is AreaEffector2D area) body.AddForce(area.Force(body));
                else if (effector is BuoyancyEffector2D buoyancy) body.AddForce(buoyancy.Force(collider, body, _state.Gravity));
            }
        }
    }

    private void Activate()
    {
        _active = this;
        RunningWorlds.Remove(this);
        RunningWorlds.Add(this);
    }

    private static bool IsActive(Component component) =>
        component.enabled && component.gameObject.activeInHierarchy;

    private void Integrate(Rigidbody2D body, Fix64 deltaTime)
    {
        if (!body.simulated || body.bodyType == RigidbodyType2D.Static ||
            body.bodyType == RigidbodyType2D.Dynamic && body.IsSleeping)
        {
            body.ConsumeAcceleration();
            body.ConsumeAngularAcceleration();
            return;
        }

        var previous = body.position;
        var previousRotation = body.rotation;
        if (body.bodyType == RigidbodyType2D.Dynamic)
        {
            var acceleration = body.ConsumeAcceleration() + _state.Gravity * body.gravityScale;
            body.linearVelocity += acceleration * deltaTime;
            body.linearVelocity *= Mathf.Clamp01(Fix64.One - body.linearDamping * deltaTime);
            body.angularVelocity += body.ConsumeAngularAcceleration() * deltaTime;
            body.angularVelocity *= Mathf.Clamp01(Fix64.One - body.angularDamping * deltaTime);
        }
        else
        {
            body.ConsumeAcceleration();
            body.ConsumeAngularAcceleration();
        }
        body.linearVelocity = ApplyPositionConstraints(body.linearVelocity, body.constraints);
        var center = body.worldCenterOfMass + body.linearVelocity * deltaTime;
        if (!body.constraints.HasFlag(RigidbodyConstraints2D.FreezeRotation))
            body.rotation += body.angularVelocity * deltaTime;
        else body.angularVelocity = Fix64.Zero;
        body.position += center - body.worldCenterOfMass;

        if (body.collisionDetectionMode is CollisionDetectionMode.Continuous or
            CollisionDetectionMode.ContinuousDynamic)
            SweepBody(body, previous, previousRotation, body.position, body.rotation);
    }

    private void SweepBody(Rigidbody2D body, Vector2 startPosition, Fix64 startRotation,
        Vector2 endPosition, Fix64 endRotation)
    {
        if (_scene is null) return;
        var own = ActiveColliders(_scene).Where(collider => ReferenceEquals(collider.attachedRigidbody, body) &&
            !collider.isTrigger).ToArray();
        if (own.Length == 0) return;
        var targets = ActiveColliders(_scene).Where(collider => !ReferenceEquals(collider.attachedRigidbody, body) &&
            !collider.isTrigger && own.Any(source => _state.ShouldCollide(source, collider))).ToArray();
        if (targets.Length == 0) return;
        var rotationDelta = endRotation - startRotation;
        var segments = Math.Clamp((int)Math.Ceiling((double)(Fix64.Abs(rotationDelta) / 10)), 1, 36);
        var targetShapes = new Dictionary<Collider2D, PhysicsShape2D>(ReferenceEqualityComparer.Instance);
        foreach (var target in targets) targetShapes[target] = PhysicsShapeFactory2D.Create(target);
        for (var segment = 0; segment < segments; segment++)
        {
            var fromTime = (Fix64)segment / segments;
            var toTime = (Fix64)(segment + 1) / segments;
            SetBodyPose(body, startPosition, startRotation, endPosition, endRotation, fromTime);
            var startShapes = new Dictionary<Collider2D, PhysicsShape2D>(ReferenceEqualityComparer.Instance);
            foreach (var collider in own) startShapes[collider] = PhysicsShapeFactory2D.Create(collider);
            var from = body.position;
            SetBodyPose(body, startPosition, startRotation, endPosition, endRotation, toTime);
            var movement = body.position - from;
            var length = movement.magnitude;
            var bestDistance = Fix64.FromRaw(long.MaxValue);
            var bestNormal = Vector2.zero;
            if (length > Fix64.Epsilon)
                foreach (var source in startShapes.Values)
                    foreach (var target in targetShapes.Values)
                        if (CollisionAlgorithms2D.SweepShape(source, movement / length, length, target,
                                out var distance, out var normal) && distance < bestDistance)
                        {
                            bestDistance = distance;
                            bestNormal = normal;
                        }
            if (bestDistance != Fix64.FromRaw(long.MaxValue))
            {
                var fraction = Mathf.Clamp01(bestDistance / length);
                var hitTime = fromTime + (toTime - fromTime) * fraction;
                SetBodyPose(body, startPosition, startRotation, endPosition, endRotation, hitTime);
                body.position -= movement.normalized * CollisionSeparationEpsilon;
                StopIntoSurface(body, bestNormal);
                return;
            }

            if (!OverlapsTargets(own, targetShapes)) continue;
            var low = fromTime;
            var high = toTime;
            for (var iteration = 0; iteration < 14; iteration++)
            {
                var middle = (low + high) * Fix64.Half;
                SetBodyPose(body, startPosition, startRotation, endPosition, endRotation, middle);
                if (OverlapsTargets(own, targetShapes)) high = middle;
                else low = middle;
            }
            SetBodyPose(body, startPosition, startRotation, endPosition, endRotation, low);
            body.angularVelocity = Fix64.Zero;
            var closest = targetShapes.Values.OrderBy(shape => (shape.Center - body.worldCenterOfMass).sqrMagnitude).First();
            StopIntoSurface(body, (closest.Center - body.worldCenterOfMass).normalized);
            return;
        }
        body.position = endPosition;
        body.rotation = endRotation;
    }

    private static void SetBodyPose(Rigidbody2D body, Vector2 startPosition, Fix64 startRotation,
        Vector2 endPosition, Fix64 endRotation, Fix64 time)
    {
        body.position = startPosition + (endPosition - startPosition) * time;
        body.rotation = startRotation + (endRotation - startRotation) * time;
    }

    private static bool OverlapsTargets(IEnumerable<Collider2D> own,
        IReadOnlyDictionary<Collider2D, PhysicsShape2D> targets)
    {
        foreach (var collider in own)
        {
            var shape = PhysicsShapeFactory2D.Create(collider);
            foreach (var target in targets.Values)
                if (CollisionAlgorithms2D.TryCollide(shape, target, out _)) return true;
        }
        return false;
    }

    private static void StopIntoSurface(Rigidbody2D body, Vector2 normal)
    {
        if (normal.sqrMagnitude <= Fix64.Epsilon) return;
        normal = normal.normalized;
        var speed = Vector2.Dot(body.linearVelocity, normal);
        if (speed > Fix64.Zero) body.linearVelocity -= normal * speed;
    }

    private static Vector2 ApplyPositionConstraints(Vector2 value, RigidbodyConstraints2D constraints) => new(
        constraints.HasFlag(RigidbodyConstraints2D.FreezePositionX) ? Fix64.Zero : value.x,
        constraints.HasFlag(RigidbodyConstraints2D.FreezePositionY) ? Fix64.Zero : value.y);

    internal static Vector2 GetClosestPoint(Collider2D collider, Vector2 point) =>
        CollisionAlgorithms2D.ClosestPoint(PhysicsShapeFactory2D.Create(collider), point);

    internal static bool OverlapPoint(Collider2D collider, Vector2 point) =>
        CollisionAlgorithms2D.Contains(PhysicsShapeFactory2D.Create(collider), point);

    internal static Bounds2D GetBounds(Collider2D collider)
    {
        var bounds = PhysicsShapeFactory2D.Create(collider).Bounds;
        return new Bounds2D(bounds.Center, bounds.Max - bounds.Min);
    }

    internal static ColliderDistance2D Distance(Collider2D colliderA, Collider2D colliderB)
    {
        var a = PhysicsShapeFactory2D.Create(colliderA);
        var b = PhysicsShapeFactory2D.Create(colliderB);
        if (CollisionAlgorithms2D.TryCollide(a, b, out var manifold))
            return new ColliderDistance2D
            {
                pointA = manifold.Point,
                pointB = manifold.Point,
                normal = manifold.Normal,
                distance = -manifold.Penetration,
                isValid = true
            };
        var pointA = CollisionAlgorithms2D.ClosestPoint(a, b.Center);
        var pointB = CollisionAlgorithms2D.ClosestPoint(b, pointA);
        pointA = CollisionAlgorithms2D.ClosestPoint(a, pointB);
        var delta = pointB - pointA;
        return new ColliderDistance2D
        {
            pointA = pointA,
            pointB = pointB,
            normal = delta.sqrMagnitude <= Fix64.Epsilon ? Vector2.right : delta.normalized,
            distance = delta.magnitude,
            isValid = true
        };
    }

    internal static bool Raycast(Vector2 origin, Vector2 direction, out RaycastHit2D hit,
        Fix64 maxDistance, ulong layerMask, QueryTriggerInteraction triggerInteraction)
    {
        hit = default;
        var active = ActiveWorld;
        return active is not null && active.RaycastInWorld(origin, direction, maxDistance,
            layerMask, triggerInteraction, out hit);
    }

    internal static RaycastHit2D[] RaycastAll(Vector2 origin, Vector2 direction, Fix64 maxDistance,
        ulong layerMask, QueryTriggerInteraction triggerInteraction) =>
        ActiveWorld?.RaycastAllInWorld(origin, direction, maxDistance, layerMask, triggerInteraction) ?? [];

    private RaycastHit2D[] RaycastAllInWorld(Vector2 origin, Vector2 direction, Fix64 maxDistance,
        ulong layerMask, QueryTriggerInteraction triggerInteraction) =>
        CollectRaycastHits(origin, direction, maxDistance, layerMask, triggerInteraction);

    private RaycastHit2D[] CollectRaycastHits(Vector2 origin, Vector2 direction, Fix64 maxDistance,
        ulong layerMask, QueryTriggerInteraction triggerInteraction)
    {
        if (_scene is null || direction.sqrMagnitude <= Fix64.Epsilon) return [];
        direction = direction.normalized;
        var hits = new List<RaycastHit2D>();
        var colliders = _scene.QueryComponents<Collider2D>();
        for (var index = 0; index < colliders.Count; index++)
        {
            var collider = colliders[index];
            if (!IsQueryable(collider) || !LayerMatches(collider, layerMask) || !TriggerMatches(collider, triggerInteraction) ||
                !CollisionAlgorithms2D.Raycast(PhysicsShapeFactory2D.Create(collider), origin, direction,
                    maxDistance, out var distance, out var normal)) continue;
            hits.Add(new RaycastHit2D
            {
                collider = collider, distance = distance,
                fraction = maxDistance <= Fix64.Epsilon ? Fix64.Zero : distance / maxDistance,
                point = origin + direction * distance, normal = normal
            });
        }
        hits.Sort(static (left, right) =>
        {
            var distance = left.distance.CompareTo(right.distance);
            return distance != 0 ? distance : left.collider.Id.CompareTo(right.collider.Id);
        });
        return hits.ToArray();
    }

    private bool RaycastInWorld(Vector2 origin, Vector2 direction, Fix64 maxDistance,
        ulong layerMask, QueryTriggerInteraction triggerInteraction, out RaycastHit2D hit)
    {
        hit = default;
        if (_scene is null || direction.sqrMagnitude <= Fix64.Epsilon) return false;
        direction = direction.normalized;
        // QueryComponents already provides a stable scene snapshot. The direct
        // closest-hit path stays allocation-free; RaycastAll retains its own
        // result-array contract for callers that explicitly request all hits.
        var colliders = _scene.QueryComponents<Collider2D>();
        for (var index = 0; index < colliders.Count; index++)
        {
            var collider = colliders[index];
            if (!IsQueryable(collider) || !LayerMatches(collider, layerMask) || !TriggerMatches(collider, triggerInteraction) ||
                !CollisionAlgorithms2D.Raycast(PhysicsShapeFactory2D.Create(collider), origin, direction,
                    maxDistance, out var distance, out var normal)) continue;
            var candidate = new RaycastHit2D
            {
                collider = collider,
                distance = distance,
                fraction = maxDistance <= Fix64.Epsilon ? Fix64.Zero : distance / maxDistance,
                point = origin + direction * distance,
                normal = normal
            };
            if (IsCloserHit(candidate, hit)) hit = candidate;
        }
        return hit.collider is not null;
    }

    internal static Collider2D[] OverlapCircle(Vector2 position, Fix64 radius, ulong layerMask,
        QueryTriggerInteraction triggerInteraction) =>
        ActiveWorld?.OverlapShapeInWorld(
            PhysicsShapeFactory2D.CreateCircle(position, radius), layerMask, triggerInteraction) ?? [];

    internal static bool CircleCast(Vector2 origin, Fix64 radius, Vector2 direction,
        out RaycastHit2D hit, Fix64 maxDistance, ulong layerMask,
        QueryTriggerInteraction triggerInteraction)
    {
        hit = default;
        var active = ActiveWorld;
        return active is not null && active.CastCircleInWorld(origin, radius, direction,
            maxDistance, layerMask, triggerInteraction, out hit);
    }

    private bool CastCircleInWorld(Vector2 origin, Fix64 radius, Vector2 direction,
        Fix64 maxDistance, ulong layerMask, QueryTriggerInteraction triggerInteraction,
        out RaycastHit2D hit)
    {
        hit = default;
        if (_scene is null || direction.sqrMagnitude <= Fix64.Epsilon) return false;
        direction = direction.normalized;
        var colliders = _scene.QueryComponents<Collider2D>();
        for (var index = 0; index < colliders.Count; index++)
        {
            var collider = colliders[index];
            if (!IsQueryable(collider) || !LayerMatches(collider, layerMask) || !TriggerMatches(collider, triggerInteraction)) continue;
            var target = PhysicsShapeFactory2D.Create(collider);
            if (!CollisionAlgorithms2D.SweepCircle(origin, radius, direction, maxDistance, target,
                    out var distance, out var normal)) continue;
            var candidate = new RaycastHit2D
            {
                collider = collider,
                distance = distance,
                fraction = maxDistance <= Fix64.Epsilon ? Fix64.Zero : distance / maxDistance,
                point = origin + direction * distance + normal * radius,
                // SweepCircle's manifold normal points from the moving circle into
                // the collider. Public cast results expose the collider's outward normal.
                normal = -normal
            };
            if (IsCloserHit(candidate, hit)) hit = candidate;
        }
        return hit.collider is not null;
    }

    private static bool IsCloserHit(RaycastHit2D candidate, RaycastHit2D current) =>
        current.collider is null || candidate.distance < current.distance ||
        (candidate.distance == current.distance && candidate.collider.Id.CompareTo(current.collider.Id) < 0);

    internal static Collider2D[] OverlapBox(Vector2 center, Vector2 size, ulong layerMask,
        QueryTriggerInteraction triggerInteraction) =>
        OverlapBox(center, size, Fix64.Zero, layerMask, triggerInteraction);

    internal static Collider2D[] OverlapBox(Vector2 center, Vector2 size, Fix64 angle,
        ulong layerMask, QueryTriggerInteraction triggerInteraction) =>
        ActiveWorld?.OverlapShapeInWorld(
            PhysicsShapeFactory2D.CreateBox(center, size, angle), layerMask, triggerInteraction) ?? [];

    internal static Collider2D? OverlapPoint(Vector2 point, ulong layerMask,
        QueryTriggerInteraction triggerInteraction)
    {
        var active = ActiveWorld;
        if (active?._scene is null) return null;
        return ActiveColliders(active._scene).FirstOrDefault(collider =>
            LayerMatches(collider, layerMask) && active.TriggerMatches(collider, triggerInteraction) &&
            CollisionAlgorithms2D.Contains(PhysicsShapeFactory2D.Create(collider), point));
    }

    private Collider2D[] OverlapShapeInWorld(PhysicsShape2D query, ulong layerMask,
        QueryTriggerInteraction triggerInteraction)
    {
        if (_scene is null) return [];
        var result = new List<Collider2D>();
        foreach (var collider in ActiveColliders(_scene))
        {
            if (!LayerMatches(collider, layerMask) || !TriggerMatches(collider, triggerInteraction)) continue;
            var shape = PhysicsShapeFactory2D.Create(collider);
            if (query.Bounds.Intersects(shape.Bounds) && CollisionAlgorithms2D.TryCollide(query, shape, out _))
                result.Add(collider);
        }
        return [.. result];
    }

    internal static void SyncTransforms() { foreach (var world in RunningWorlds) world._scene?.SynchronizeTransforms(); }

    private static void FilterActive<T>(List<T> values) where T : Component
    {
        var write = 0;
        foreach (var value in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(values)) if (IsActive(value)) values[write++] = value;
        values.RemoveRange(write, values.Count - write);
    }
    private void SortById<T>(List<T> values) where T : Component
    {
        _sortBatch.Clear(); _sortBatch.EnsureCapacity(values.Count);
        for (var i = 0; i < values.Count; i++) _sortBatch.Add(new(values[i], values[i].Id, i));
        _sortBatch.WrittenSpan.Sort(ComponentComparison);
        for (var i = 0; i < values.Count; i++) values[i] = (T)_sortBatch[i].Component;
        _sortBatch.Clear();
    }
    private static int CompareComponents(ComponentSortEntry a, ComponentSortEntry b)
    { var result = a.Id.CompareTo(b.Id); return result != 0 ? result : a.Order.CompareTo(b.Order); }
    private void FillActiveColliders(List<Collider2D> values)
    {
        _scene!.GetComponents(values); var write = 0;
        foreach (var collider in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(values))
            if (IsActive(collider) && collider.attachedRigidbody?.simulated != false && collider.compositeOwner is not { enabled: true }) values[write++] = collider;
        values.RemoveRange(write, values.Count - write); SortById(values);
    }

    private static Collider2D[] ActiveColliders(Scene scene) =>
        [.. scene.QueryComponents<Collider2D>().Where(IsQueryable).OrderBy(item => item.Id)];

    private static bool IsQueryable(Collider2D collider) => IsActive(collider) &&
        collider.attachedRigidbody?.simulated != false && collider.compositeOwner is not { enabled: true };

    private static bool LayerMatches(Collider2D collider, ulong mask) =>
        (mask & SortingLayer.ToMask(collider.gameObject.layer)) != 0;

    private bool TriggerMatches(Collider2D collider, QueryTriggerInteraction value) => value switch
    {
        QueryTriggerInteraction.Ignore => !collider.isTrigger,
        QueryTriggerInteraction.Collide => true,
        _ => _state.QueriesHitTriggers || !collider.isTrigger
    };

    private static void Dispatch(ContactState state, ContactPhase phase)
    {
        if (state.IsTrigger)
        {
            Invoke(state.Left.gameObject, $"OnTrigger{phase}2D", state.Right);
            Invoke(state.Right.gameObject, $"OnTrigger{phase}2D", state.Left);
            return;
        }
        var method = phase switch { ContactPhase.Enter => "OnCollisionEnter2D", ContactPhase.Stay => "OnCollisionStay2D", _ => "OnCollisionExit2D" };
        InvokeCollision(state.Left, state.Right, state.Group, false, method);
        InvokeCollision(state.Right, state.Left, state.Group, true, method);
    }

    private static void DispatchExits(IEnumerable<ContactState> states)
    {
        foreach (var state in states) Dispatch(state, ContactPhase.Exit);
    }

    private ContactState[] RentContactGroup(int count)
    {
        if (count == 1)
            return _singleContactGroups.Count > 0 ? _singleContactGroups.Pop() : new ContactState[1];
        if (count == 2)
            return _doubleContactGroups.Count > 0 ? _doubleContactGroups.Pop() : new ContactState[2];
        return new ContactState[count];
    }

    private ContactState RentContactState(Collider2D left, Collider2D right,
        PhysicsManifold2D manifold, bool isTrigger, int leftPart, int rightPart)
    {
        var state = _contactStatePool.Count > 0 ? _contactStatePool.Pop() : new ContactState();
        state.Initialize(left, right, manifold, isTrigger, leftPart, rightPart);
        return state;
    }

    private void ReleaseContactGroups(Dictionary<ContactKey, ContactState> contacts)
    {
        foreach (var state in contacts.Values)
        {
            var group = state.Group;
            for (var i = 0; i < group.Length; i++)
            {
                var contact = group[i];
                group[i] = null!;
                contact.Group = [];
                contact.Left = null!; contact.Right = null!;
                contact.LeftBody = contact.RightBody = null;
                _contactStatePool.Push(contact);
            }
            if (group.Length == 1) _singleContactGroups.Push(group);
            else if (group.Length == 2) _doubleContactGroups.Push(group);
        }
        contacts.Clear();
    }

    private static Collision2D CreateCollision(Collider2D own, Collider2D other,
        ContactState[] states, bool flip)
    {
        var count = 0;
        foreach (var state in states) count += state.Manifold.HasSecondPoint ? 2 : 1;
        var points = new ContactPoint2D[count]; var index = 0;
        foreach (var state in states)
        {
            var manifold = state.Manifold;
            for (var i = 0; i < (manifold.HasSecondPoint ? 2 : 1); i++) points[index++] = new()
            {
                point = i == 0 ? manifold.Point : manifold.Point2,
                normal = flip ? -manifold.Normal : manifold.Normal,
                separation = -manifold.Penetration,
                thisCollider = own,
                otherCollider = other
            };
        }
        return new() { collider = other, contacts = points,
            relativeVelocity = (other.attachedRigidbody?.linearVelocity ?? Vector2.zero) -
                               (own.attachedRigidbody?.linearVelocity ?? Vector2.zero) };
    }

    private static void InvokeCollision(Collider2D own, Collider2D other, ContactState[] states, bool flip, string methodName)
    {
        Collision2D? collision = null;
        using var components = own.gameObject.GetComponentsEnumeratorUnchecked();
        while (components.MoveNext())
        {
            var component = components.Current;
            if (component is not MonoBehaviour { enabled: true } behaviour) continue;
            try
            {
                if (RuntimeTypeCache.CanInvokeMessage(behaviour.GetType(), methodName, typeof(Collision2D)))
                    RuntimeTypeCache.TryInvokeMessage(behaviour, methodName, collision ??= CreateCollision(own, other, states, flip));
            }
            catch (Exception exception) { Debug.LogError($"{behaviour.GetType().FullName}.{methodName} failed: {exception.Message}"); }
        }
    }

    private static void Invoke(GameObject target, string methodName, object argument)
    {
        using var components = target.GetComponentsEnumeratorUnchecked();
        while (components.MoveNext())
        {
            var component = components.Current;
            if (component is not MonoBehaviour { enabled: true } behaviour) continue;
            try { RuntimeTypeCache.TryInvokeMessage(behaviour, methodName, argument); }
            catch (Exception exception)
            {
                Debug.LogError($"{behaviour.GetType().FullName}.{methodName} failed: {exception.Message}");
            }
        }
    }

    private readonly record struct ContactKey(Guid Left, Guid Right)
    {
        internal static ContactKey Create(Collider2D left, Collider2D right) =>
            left.Id.CompareTo(right.Id) < 0 ? new(left.Id, right.Id) : new(right.Id, left.Id);
    }

    private sealed class ContactState
    {
        internal Collider2D Left = null!, Right = null!;
        internal PhysicsManifold2D Manifold;
        internal bool IsTrigger;
        internal int LeftPart, RightPart;
        internal ContactState[] Group = [];
        internal int PointStart, PointCount;
        internal Rigidbody2D? LeftBody, RightBody;
        internal Fix64 LeftInverseMass, RightInverseMass, LeftInverseInertia, RightInverseInertia;
        internal Vector2 LeftCenter, RightCenter;
        internal Fix64 Friction, K11, K22, K12, Determinant;
        internal Fix64 DeltaTime;

        internal void Initialize(Collider2D left, Collider2D right, PhysicsManifold2D manifold,
            bool isTrigger, int leftPart, int rightPart)
        {
            Left = left; Right = right; Manifold = manifold; IsTrigger = isTrigger;
            LeftPart = leftPart; RightPart = rightPart; Group = [];
            PointStart = PointCount = 0; LeftBody = RightBody = null;
            LeftInverseMass = RightInverseMass = LeftInverseInertia = RightInverseInertia = Fix64.Zero;
            LeftCenter = RightCenter = Vector2.zero;
            Friction = K11 = K22 = K12 = Determinant = DeltaTime = Fix64.Zero;
        }
    }

    private enum ContactPhase { Enter, Stay, Exit }
}
