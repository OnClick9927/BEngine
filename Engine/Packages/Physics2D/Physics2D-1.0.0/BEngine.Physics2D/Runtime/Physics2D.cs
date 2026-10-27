namespace BEngine.Physics2D;

public static class Physics2D
{
    private static readonly PhysicsWorld2DState DefaultState = new();
    private static readonly Fix64 DefaultQueryDistance = Fix64.Parse("100000");

    public static Vector2 gravity
    {
        get => CurrentState.Gravity;
        set => CurrentState.Gravity = value;
    }

    public static bool queriesHitTriggers
    {
        get => CurrentState.QueriesHitTriggers;
        set => CurrentState.QueriesHitTriggers = value;
    }

    public static bool autoSimulation
    {
        get => CurrentState.AutoSimulation;
        set => CurrentState.AutoSimulation = value;
    }

    public static int velocityIterations
    {
        get => CurrentState.VelocityIterations;
        set => CurrentState.VelocityIterations = Math.Clamp(value, 1, 32);
    }

    public static int positionIterations
    {
        get => CurrentState.PositionIterations;
        set => CurrentState.PositionIterations = Math.Clamp(value, 1, 32);
    }

    public static LayerMask allLayers => new(ulong.MaxValue);

    public static bool Raycast(Vector2 origin, Vector2 direction, out RaycastHit2D hitInfo,
        Fix64 maxDistance = default, ulong layerMask = ulong.MaxValue,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        PhysicsWorld2D.Raycast(origin, direction, out hitInfo,
            maxDistance <= Fix64.Zero ? DefaultQueryDistance : maxDistance,
            layerMask, queryTriggerInteraction);

    public static RaycastHit2D[] RaycastAll(Vector2 origin, Vector2 direction,
        Fix64 maxDistance = default, ulong layerMask = ulong.MaxValue,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        PhysicsWorld2D.RaycastAll(origin, direction,
            maxDistance <= Fix64.Zero ? DefaultQueryDistance : maxDistance,
            layerMask, queryTriggerInteraction);

    public static RaycastHit2D Raycast(Vector2 origin, Vector2 direction,
        Fix64 distance = default, ulong layerMask = ulong.MaxValue) =>
        Raycast(origin, direction, out var hit, distance, layerMask) ? hit : default;

    /// <summary>Queries the specified running Scene without changing the caller's scene context.</summary>
    public static RaycastHit2D Raycast(Scene scene, Vector2 origin, Vector2 direction,
        Fix64 distance = default, ulong layerMask = ulong.MaxValue,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ObjectDisposedException.ThrowIf(!scene.isCreated, scene);
        return PhysicsWorld2D.Raycast(scene, origin, direction, out var hit,
            distance <= Fix64.Zero ? DefaultQueryDistance : distance, layerMask, queryTriggerInteraction) ? hit : default;
    }

    public static int Raycast(Vector2 origin, Vector2 direction, ContactFilter2D contactFilter,
        RaycastHit2D[] results, Fix64 distance = default)
    {
        ArgumentNullException.ThrowIfNull(results);
        var hits = RaycastAll(origin, direction, distance,
            contactFilter.useLayerMask ? contactFilter.layerMask.value : ulong.MaxValue,
            contactFilter.useTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore);
        var count = Math.Min(results.Length, hits.Length);
        Array.Copy(hits, results, count);
        return count;
    }

    public static Collider2D[] OverlapCircle(Vector2 point, Fix64 radius,
        ulong layerMask = ulong.MaxValue,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        PhysicsWorld2D.OverlapCircle(point, radius, layerMask, queryTriggerInteraction);

    public static bool CheckCircle(Vector2 point, Fix64 radius, ulong layerMask = ulong.MaxValue,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        OverlapCircle(point, radius, layerMask, queryTriggerInteraction).Length > 0;

    public static Collider2D[] OverlapBox(Vector2 point, Vector2 size,
        ulong layerMask = ulong.MaxValue,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        PhysicsWorld2D.OverlapBox(point, size, layerMask, queryTriggerInteraction);

    public static bool CheckBox(Vector2 point, Vector2 size, ulong layerMask = ulong.MaxValue,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        OverlapBox(point, size, layerMask, queryTriggerInteraction).Length > 0;

    public static Collider2D? OverlapPoint(Vector2 point, ulong layerMask = ulong.MaxValue,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        PhysicsWorld2D.OverlapPoint(point, layerMask, queryTriggerInteraction);

    public static Collider2D[] OverlapBoxAll(Vector2 point, Vector2 size, Fix64 angle,
        ulong layerMask = ulong.MaxValue,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        PhysicsWorld2D.OverlapBox(point, size, angle, layerMask, queryTriggerInteraction);

    public static bool CircleCast(Vector2 origin, Fix64 radius, Vector2 direction,
        out RaycastHit2D hitInfo, Fix64 maxDistance = default, ulong layerMask = ulong.MaxValue,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        PhysicsWorld2D.CircleCast(origin, radius, direction, out hitInfo,
            maxDistance <= Fix64.Zero ? DefaultQueryDistance : maxDistance,
            layerMask, queryTriggerInteraction);

    public static void Simulate(Fix64 step)
    {
        if (step <= Fix64.Zero) throw new ArgumentOutOfRangeException(nameof(step));
        PhysicsWorld2D.SimulateActive(step);
    }

    public static void IgnoreCollision(Collider2D collider1, Collider2D collider2, bool ignore = true)
    {
        ArgumentNullException.ThrowIfNull(collider1);
        ArgumentNullException.ThrowIfNull(collider2);
        PhysicsWorld2D.StateFor(collider1, collider2).SetIgnore(collider1, collider2, ignore);
    }

    public static void IgnoreLayerCollision(ulong layer1, ulong layer2, bool ignore = true) =>
        CurrentState.SetIgnoreLayerCollision(layer1, layer2, ignore);

    public static bool GetIgnoreLayerCollision(ulong layer1, ulong layer2) =>
        CurrentState.GetIgnoreLayerCollision(layer1, layer2);

    public static ColliderDistance2D Distance(Collider2D colliderA, Collider2D colliderB)
    {
        ArgumentNullException.ThrowIfNull(colliderA);
        ArgumentNullException.ThrowIfNull(colliderB);
        return PhysicsWorld2D.Distance(colliderA, colliderB);
    }

    public static bool GetIgnoreCollision(Collider2D collider1, Collider2D collider2)
    {
        ArgumentNullException.ThrowIfNull(collider1);
        ArgumentNullException.ThrowIfNull(collider2);
        return PhysicsWorld2D.StateFor(collider1, collider2).ShouldIgnore(collider1, collider2);
    }

    public static void SyncTransforms() => PhysicsWorld2D.SyncTransforms();

    internal static PhysicsWorld2DState defaultState => DefaultState;

    private static PhysicsWorld2DState CurrentState => PhysicsWorld2D.activeState ?? DefaultState;
}
