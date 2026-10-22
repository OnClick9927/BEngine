namespace BEngine.Navigation2D;

public readonly struct NavigationHit2D
{
    public Vector2 position { get; init; }
    public Fix64 distance { get; init; }
    public ulong mask { get; init; }
}
