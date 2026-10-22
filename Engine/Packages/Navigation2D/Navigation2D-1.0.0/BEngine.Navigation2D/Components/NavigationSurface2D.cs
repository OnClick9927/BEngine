namespace BEngine.Navigation2D;

[DisallowMultipleComponent]
[AddComponentMenu("Navigation 2D/Navigation Surface 2D")]
public sealed partial class NavigationSurface2D : MonoBehaviour
{
    private NavigationGrid2D? _grid;

    public int agentTypeId { get; set; }
    public CollectObjects collectObjects { get; set; } = CollectObjects.Volume;
    public Vector2 center { get; set; } = Vector2.zero;
    public Vector2 size { get; set; } = new(20, 20);
    public ulong layerMask { get; set; } = ulong.MaxValue;
    public NavigationCollectGeometry2D useGeometry { get; set; } =
        NavigationCollectGeometry2D.PhysicsColliders;
    public Fix64 cellSize { get; set; } = Fix64.Half;
    public Fix64 agentRadius { get; set; } = Fix64.Half;
    public bool buildOnStart { get; set; } = true;
    public bool hasData => _grid is not null;

    public override void Start()
    {
        if (buildOnStart) BuildNavigation();
    }

    [ContextMenu("Bake")]
    public void BuildNavigation()
    {
        CancelSearches();
        _grid = NavigationGrid2D.Bake(this);
        Navigation2D.InvalidatePaths();
    }

    [ContextMenu("Clear")]
    public void RemoveData() { CancelSearches(); _grid = null; Navigation2D.InvalidatePaths(); }
    public void UpdateNavigation() => BuildNavigation();

    internal bool CalculatePath(Vector2 source, Vector2 target, NavigationPath2D path,
        ulong areaMask = Navigation2D.AllAreas) => _grid?.CalculatePath(source, target, path, areaMask) == true;

    internal bool Sample(Vector2 source, Fix64 maxDistance, out NavigationHit2D hit,
        ulong areaMask = Navigation2D.AllAreas)
    {
        if (_grid is not null) return _grid.Sample(source, maxDistance, out hit, areaMask);
        hit = default;
        return false;
    }
}
