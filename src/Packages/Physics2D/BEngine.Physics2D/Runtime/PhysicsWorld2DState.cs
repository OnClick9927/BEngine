namespace BEngine.Physics2D;

internal sealed class PhysicsWorld2DState
{
    private readonly HashSet<CollisionPair> _ignoredPairs = [];

    public Vector2 Gravity { get; set; } = Vector2.down * Fix64.Parse("9.81");
    public bool QueriesHitTriggers { get; set; } = true;
    public bool AutoSimulation { get; set; } = true;
    public int VelocityIterations { get; set; } = 4;

    public void CopySettingsFrom(PhysicsWorld2DState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Gravity = source.Gravity;
        QueriesHitTriggers = source.QueriesHitTriggers;
        AutoSimulation = source.AutoSimulation;
        VelocityIterations = source.VelocityIterations;
        _ignoredPairs.Clear();
    }

    public void SetIgnore(Collider2D left, Collider2D right, bool ignore)
    {
        var pair = CollisionPair.Create(left, right);
        if (ignore) _ignoredPairs.Add(pair);
        else _ignoredPairs.Remove(pair);
    }

    public bool ShouldIgnore(Collider2D left, Collider2D right) =>
        _ignoredPairs.Contains(CollisionPair.Create(left, right));

    public void ClearIgnoredPairs() => _ignoredPairs.Clear();

    private readonly record struct CollisionPair(int LeftInstanceId, int RightInstanceId)
    {
        public static CollisionPair Create(Collider2D left, Collider2D right)
        {
            var leftId = left.GetInstanceID();
            var rightId = right.GetInstanceID();
            return leftId < rightId ? new(leftId, rightId) : new(rightId, leftId);
        }
    }
}
