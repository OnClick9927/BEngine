namespace BEngine.Physics2D;

internal sealed partial class PhysicsWorld2DState
{
    private readonly HashSet<CollisionPair> _ignoredPairs = [];
    private readonly HashSet<LayerPair> _ignoredLayerPairs = [];

    public Vector2 Gravity { get; set; } = Vector2.down * Fix64.Parse("9.81");
    public bool QueriesHitTriggers { get; set; } = true;
    public bool AutoSimulation { get; set; } = true;
    public int VelocityIterations { get; set; } = 4;
    public int PositionIterations { get; set; } = 3;

    public void CopySettingsFrom(PhysicsWorld2DState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Gravity = source.Gravity;
        QueriesHitTriggers = source.QueriesHitTriggers;
        AutoSimulation = source.AutoSimulation;
        VelocityIterations = source.VelocityIterations;
        PositionIterations = source.PositionIterations;
        _ignoredPairs.Clear();
        _ignoredLayerPairs.Clear();
        foreach (var pair in source._ignoredLayerPairs) _ignoredLayerPairs.Add(pair);
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

    public void SetIgnoreLayerCollision(ulong left, ulong right, bool ignore)
    {
        SortingLayer.Validate(left);
        SortingLayer.Validate(right);
        var pair = LayerPair.Create(left, right);
        if (ignore) _ignoredLayerPairs.Add(pair);
        else _ignoredLayerPairs.Remove(pair);
    }

    public bool GetIgnoreLayerCollision(ulong left, ulong right) =>
        _ignoredLayerPairs.Contains(LayerPair.Create(left, right));

    public bool ShouldCollide(Collider2D left, Collider2D right) =>
        !ShouldIgnore(left, right) &&
        !GetIgnoreLayerCollision(left.gameObject.layer, right.gameObject.layer);

    private readonly record struct CollisionPair(int LeftInstanceId, int RightInstanceId)
    {
        public static CollisionPair Create(Collider2D left, Collider2D right)
        {
            var leftId = left.GetInstanceID();
            var rightId = right.GetInstanceID();
            return leftId < rightId ? new(leftId, rightId) : new(rightId, leftId);
        }
    }

    private readonly record struct LayerPair(ulong Left, ulong Right)
    {
        public static LayerPair Create(ulong left, ulong right) =>
            left < right ? new(left, right) : new(right, left);
    }
}
