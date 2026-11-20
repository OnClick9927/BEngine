namespace BEngine.Physics2D;

public sealed partial class PhysicsWorld2D
{
    private const int MaximumRetainedWorkspaceCapacity = 4096;
    private ObjectPool<SimulationWorkspace>? _simulationWorkspaces;
    private ObjectPool<QueryWorkspace>? _queryWorkspaces;

    // Each call retains its own rental until all shape callbacks have finished.
    // A CompositeCollider2D.GetPaths override can re-enter queries or stop the world.
    private ObjectPool<SimulationWorkspace> SimulationWorkspaces => _simulationWorkspaces ??=
        new(static () => new(), onRelease: static workspace => workspace.Reset(), maxSize: 1);

    private ObjectPool<QueryWorkspace> QueryWorkspaces => _queryWorkspaces ??=
        new(static () => new(), onRelease: static workspace => workspace.Reset(), maxSize: 4);

    private void DisposeWorkspaces()
    {
        // Outstanding calls keep their local pool reference and destroy their
        // cleared rental on return. A subsequent Start creates fresh pools.
        var simulation = _simulationWorkspaces; _simulationWorkspaces = null;
        var query = _queryWorkspaces; _queryWorkspaces = null;
        simulation?.Dispose(); query?.Dispose();
    }

    private static void ResetList<T>(List<T> list)
    {
        list.Clear();
        if (list.Capacity > MaximumRetainedWorkspaceCapacity) list.Capacity = 0;
    }

    private static void ResetShapes(Dictionary<Collider2D, PhysicsShape2D> shapes)
    {
        shapes.Clear();
        if (shapes.EnsureCapacity(0) > MaximumRetainedWorkspaceCapacity) shapes.TrimExcess();
    }

    private sealed class SimulationWorkspace
    {
        internal readonly HashSet<Rigidbody2D> Affected = new(ReferenceEqualityComparer.Instance);
        internal readonly List<Collider2D> Own = [];
        internal readonly List<Collider2D> Targets = [];
        internal readonly Dictionary<Collider2D, PhysicsShape2D> TargetShapes = new(ReferenceEqualityComparer.Instance);
        internal readonly Dictionary<Collider2D, PhysicsShape2D> StartShapes = new(ReferenceEqualityComparer.Instance);

        internal void Reset()
        {
            Affected.Clear();
            if (Affected.EnsureCapacity(0) > MaximumRetainedWorkspaceCapacity) Affected.TrimExcess();
            ResetList(Own); ResetList(Targets); ResetShapes(TargetShapes); ResetShapes(StartShapes);
        }
    }

    private sealed class QueryWorkspace
    {
        internal readonly List<Collider2D> Colliders = [];
        internal readonly List<Collider2D> Results = [];
        internal readonly List<(RaycastHit2D Hit, int Order)> Hits = [];

        internal void Reset()
        {
            ResetList(Colliders); ResetList(Results); ResetList(Hits);
        }
    }
}
