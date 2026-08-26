namespace BEngine.Physics2D;

public static class Physics2D
{
    private static readonly PhysicsWorld2DState DefaultState = new();

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
        set => CurrentState.VelocityIterations = value;
    }

    public static bool Raycast(Vector2 origin, Vector2 direction, out RaycastHit2D hitInfo,
        Fix64 maxDistance = default, ulong layerMask = ulong.MaxValue,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        PhysicsWorld2D.Raycast(origin, direction, out hitInfo,
            maxDistance <= Fix64.Zero ? Fix64.Parse("100000") : maxDistance,
            layerMask, queryTriggerInteraction);

    public static RaycastHit2D[] RaycastAll(Vector2 origin, Vector2 direction,
        Fix64 maxDistance = default, ulong layerMask = ulong.MaxValue,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        PhysicsWorld2D.RaycastAll(origin, direction,
            maxDistance <= Fix64.Zero ? Fix64.Parse("100000") : maxDistance,
            layerMask, queryTriggerInteraction);

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

    public static bool CircleCast(Vector2 origin, Fix64 radius, Vector2 direction,
        out RaycastHit2D hitInfo, Fix64 maxDistance = default, ulong layerMask = ulong.MaxValue,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        PhysicsWorld2D.CircleCast(origin, radius, direction, out hitInfo,
            maxDistance <= Fix64.Zero ? Fix64.Parse("100000") : maxDistance,
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
