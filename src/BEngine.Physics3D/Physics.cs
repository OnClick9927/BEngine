namespace BEngine.Physics3D;

public static class Physics
{
    private static readonly HashSet<(Guid Left, Guid Right)> IgnoredPairs = [];
    public static Vector3 gravity { get; set; } = new(0, Fix64.Parse("-9.81"), 0);
    public static bool queriesHitTriggers { get; set; } = true;
    public static bool autoSimulation { get; set; } = true;
    public static int defaultSolverIterations { get; set; } = 4;

    public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo,
        Fix64 maxDistance = default, int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        PhysicsWorld.Raycast(origin, direction, out hitInfo,
            maxDistance <= Fix64.Zero ? Fix64.Parse("100000") : maxDistance,
            layerMask, queryTriggerInteraction);

    public static bool Raycast(Vector3 origin, Vector3 direction, Fix64 maxDistance = default,
        int layerMask = ~0, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        Raycast(origin, direction, out _, maxDistance, layerMask, queryTriggerInteraction);

    public static RaycastHit[] RaycastAll(Vector3 origin, Vector3 direction,
        Fix64 maxDistance = default, int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        PhysicsWorld.RaycastAll(origin, direction,
            maxDistance <= Fix64.Zero ? Fix64.Parse("100000") : maxDistance,
            layerMask, queryTriggerInteraction);

    public static Collider[] OverlapSphere(Vector3 position, Fix64 radius, int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        PhysicsWorld.OverlapSphere(position, radius, layerMask, queryTriggerInteraction);

    public static bool CheckSphere(Vector3 position, Fix64 radius, int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        OverlapSphere(position, radius, layerMask, queryTriggerInteraction).Length > 0;

    public static Collider[] OverlapBox(Vector3 center, Vector3 halfExtents, int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        PhysicsWorld.OverlapBox(center, halfExtents, layerMask, queryTriggerInteraction);

    public static bool CheckBox(Vector3 center, Vector3 halfExtents, int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        OverlapBox(center, halfExtents, layerMask, queryTriggerInteraction).Length > 0;

    public static bool SphereCast(Vector3 origin, Fix64 radius, Vector3 direction, out RaycastHit hitInfo,
        Fix64 maxDistance = default, int layerMask = ~0,
        QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal) =>
        PhysicsWorld.SphereCast(origin, radius, direction, out hitInfo,
            maxDistance <= Fix64.Zero ? Fix64.Parse("100000") : maxDistance, layerMask, queryTriggerInteraction);

    public static void Simulate(Fix64 step)
    {
        if (step <= Fix64.Zero) throw new ArgumentOutOfRangeException(nameof(step));
        PhysicsWorld.SimulateActive(step);
    }

    public static void IgnoreCollision(Collider collider1, Collider collider2, bool ignore = true)
    {
        ArgumentNullException.ThrowIfNull(collider1);
        ArgumentNullException.ThrowIfNull(collider2);
        var key = Pair(collider1, collider2);
        if (ignore) IgnoredPairs.Add(key); else IgnoredPairs.Remove(key);
    }

    public static bool GetIgnoreCollision(Collider collider1, Collider collider2) =>
        IgnoredPairs.Contains(Pair(collider1, collider2));

    public static void SyncTransforms() => PhysicsWorld.SyncTransforms();

    internal static bool ShouldIgnore(Collider left, Collider right) => IgnoredPairs.Contains(Pair(left, right));
    private static (Guid Left, Guid Right) Pair(Collider left, Collider right) =>
        left.Id.CompareTo(right.Id) < 0 ? (left.Id, right.Id) : (right.Id, left.Id);
}
