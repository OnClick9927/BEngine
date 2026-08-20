namespace BEngine.Physics2D;

public static class Physics2D
{
    private static readonly HashSet<(Guid Left, Guid Right)> IgnoredPairs = [];

    public static Vector2 gravity { get; set; } = Vector2.down * Fix64.Parse("9.81");
    public static bool queriesHitTriggers { get; set; } = true;
    public static bool autoSimulation { get; set; } = true;
    public static int velocityIterations { get; set; } = 4;

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
        var key = Pair(collider1, collider2);
        if (ignore) IgnoredPairs.Add(key); else IgnoredPairs.Remove(key);
    }

    public static bool GetIgnoreCollision(Collider2D collider1, Collider2D collider2) =>
        IgnoredPairs.Contains(Pair(collider1, collider2));

    public static void SyncTransforms() => PhysicsWorld2D.SyncTransforms();

    internal static bool ShouldIgnore(Collider2D left, Collider2D right) =>
        IgnoredPairs.Contains(Pair(left, right));

    private static (Guid Left, Guid Right) Pair(Collider2D left, Collider2D right) =>
        left.Id.CompareTo(right.Id) < 0 ? (left.Id, right.Id) : (right.Id, left.Id);
}
