using BEngine;
using BEngine.Physics3D;

namespace BEngine.Navigation;

[DisallowMultipleComponent]
[AddComponentMenu("Navigation/NavMesh Surface")]
public sealed class NavMeshSurface : MonoBehaviour
{
    private NavMeshGrid? _grid;

    public int agentTypeID { get; set; }
    public CollectObjects collectObjects { get; set; } = CollectObjects.Volume;
    public Vector3 center { get; set; } = Vector3.zero;
    public Vector3 size { get; set; } = new(20, 5, 20);
    public int layerMask { get; set; } = ~0;
    public NavMeshCollectGeometry useGeometry { get; set; } = NavMeshCollectGeometry.PhysicsColliders;
    public Fix64 overrideTileSize { get; set; } = Fix64.One;
    public Fix64 voxelSize { get; set; } = Fix64.Half;
    public Fix64 agentRadius { get; set; } = Fix64.Half;
    public Fix64 agentHeight { get; set; } = 2;
    public Fix64 agentSlope { get; set; } = 45;
    public Fix64 agentClimb { get; set; } = Fix64.Parse("0.4");
    public bool buildOnStart { get; set; } = true;
    public bool hasData => _grid is not null;

    public override void Start()
    {
        if (buildOnStart) BuildNavMesh();
    }

    [ContextMenu("Bake")]
    public void BuildNavMesh() => _grid = NavMeshGrid.Bake(this);
    [ContextMenu("Clear")]
    public void RemoveData() => _grid = null;
    public void UpdateNavMesh() => BuildNavMesh();

    internal bool CalculatePath(Vector3 source, Vector3 target, NavMeshPath path) =>
        _grid?.CalculatePath(source, target, path) == true;
    internal bool Sample(Vector3 source, Fix64 maxDistance, out NavMeshHit hit)
    {
        if (_grid is not null) return _grid.Sample(source, maxDistance, out hit);
        hit = default;
        return false;
    }
}

[DisallowMultipleComponent]
[AddComponentMenu("Navigation/NavMesh Agent")]
public sealed class NavMeshAgent : Behaviour
{
    private NavMeshPath _path = new();
    private int _cornerIndex;
    private Vector3 _destination;

    public Fix64 radius { get; set; } = Fix64.Half;
    public Fix64 height { get; set; } = 2;
    public Fix64 speed { get; set; } = Fix64.Parse("3.5");
    public Fix64 acceleration { get; set; } = 8;
    public Fix64 angularSpeed { get; set; } = 120;
    public Fix64 stoppingDistance { get; set; } = Fix64.Parse("0.1");
    public bool autoBraking { get; set; } = true;
    public bool autoRepath { get; set; } = true;
    public bool updatePosition { get; set; } = true;
    public bool updateRotation { get; set; } = true;
    public int areaMask { get; set; } = ~0;
    public int agentTypeID { get; set; }
    public ObstacleAvoidanceType obstacleAvoidanceType { get; set; } = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
    public Vector3 velocity { get; set; }
    public Vector3 desiredVelocity { get; private set; }
    public Vector3 destination { get => _destination; set => SetDestination(value); }
    public bool hasPath => _path.status != NavMeshPathStatus.PathInvalid && _cornerIndex < _path.corners.Length;
    public bool pathPending { get; private set; }
    public bool isStopped { get; set; }
    public NavMeshPathStatus pathStatus => _path.status;
    public Fix64 remainingDistance
    {
        get
        {
            if (!hasPath) return Fix64.Zero;
            var corners = _path.corners;
            var total = Vector3.Distance(transform.position, corners[_cornerIndex]);
            for (var index = _cornerIndex; index < corners.Length - 1; index++)
                total += Vector3.Distance(corners[index], corners[index + 1]);
            return total;
        }
    }

    public bool SetDestination(Vector3 target)
    {
        _destination = target;
        pathPending = true;
        var path = new NavMeshPath();
        var result = NavMesh.CalculatePath(transform.position, target, areaMask, path);
        pathPending = false;
        if (!result) { ResetPath(); return false; }
        _path = path;
        _cornerIndex = Math.Min(1, _path.corners.Length - 1);
        return true;
    }

    public bool SetPath(NavMeshPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.status == NavMeshPathStatus.PathInvalid) return false;
        _path = path;
        _cornerIndex = Math.Min(1, path.corners.Length - 1);
        return true;
    }

    public void ResetPath() { _path = new NavMeshPath(); _cornerIndex = 0; velocity = Vector3.zero; desiredVelocity = Vector3.zero; }
    public bool Warp(Vector3 newPosition) { transform.position = newPosition; ResetPath(); return true; }

    internal void Tick(Fix64 deltaTime)
    {
        if (!enabled || isStopped || !hasPath) { desiredVelocity = Vector3.zero; velocity = Vector3.zero; return; }
        var corners = _path.corners;
        var target = corners[_cornerIndex];
        var delta = target - transform.position;
        delta = new Vector3(delta.x, 0, delta.z);
        if (delta.magnitude <= Mathf.Max(stoppingDistance, voxelTolerance))
        {
            _cornerIndex++;
            if (_cornerIndex >= corners.Length) { ResetPath(); return; }
            target = corners[_cornerIndex];
            delta = new Vector3(target.x - transform.position.x, 0, target.z - transform.position.z);
        }
        desiredVelocity = delta.normalized * speed;
        velocity = Vector3.MoveTowards(velocity, desiredVelocity, acceleration * deltaTime);
        if (updatePosition) transform.position += velocity * deltaTime;
        if (updateRotation && velocity.sqrMagnitude > Fix64.Epsilon)
            transform.localEulerAngles = new Vector3(transform.localEulerAngles.x,
                Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg, transform.localEulerAngles.z);
    }

    private static readonly Fix64 voxelTolerance = Fix64.Parse("0.05");
}

[DisallowMultipleComponent]
[AddComponentMenu("Navigation/NavMesh Obstacle")]
public sealed class NavMeshObstacle : Behaviour
{
    public NavMeshObstacleShape shape { get; set; } = NavMeshObstacleShape.Box;
    public Vector3 center { get; set; }
    public Vector3 size { get; set; } = Vector3.one;
    public Fix64 radius { get; set; } = Fix64.Half;
    public Fix64 height { get; set; } = 2;
    public bool carving { get; set; } = true;
    public bool carveOnlyStationary { get; set; } = true;

    internal bool Contains(Vector3 point, Fix64 padding)
    {
        var local = transform.InverseTransformPoint(point) - center;
        return shape == NavMeshObstacleShape.Box
            ? Mathf.Abs(local.x) <= size.x / 2 + padding && Mathf.Abs(local.z) <= size.z / 2 + padding
            : local.x * local.x + local.z * local.z <= (radius + padding) * (radius + padding);
    }
}

[AddComponentMenu("Navigation/NavMesh Link")]
public sealed class NavMeshLink : Behaviour
{
    public Vector3 startPoint { get; set; } = Vector3.left;
    public Vector3 endPoint { get; set; } = Vector3.right;
    public Fix64 width { get; set; }
    public int costModifier { get; set; } = -1;
    public bool bidirectional { get; set; } = true;
    public int area { get; set; }
    public Vector3 worldStart => transform.TransformPoint(startPoint);
    public Vector3 worldEnd => transform.TransformPoint(endPoint);
}

[AddComponentMenu("Navigation/NavMesh Modifier")]
public sealed class NavMeshModifier : Behaviour
{
    public bool overrideArea { get; set; }
    public int area { get; set; }
    public bool affectedByAgentType { get; set; }
    public int agentTypeID { get; set; }

    internal bool Blocks(Vector3 point, int surfaceAgentType)
    {
        if (!enabled || !overrideArea || area != 1 || affectedByAgentType && agentTypeID != surfaceAgentType) return false;
        if (GetComponent<Collider>() is { } collider)
        {
            var bounds = collider.bounds;
            return point.x >= bounds.min.x && point.x <= bounds.max.x &&
                   point.z >= bounds.min.z && point.z <= bounds.max.z;
        }
        var local = transform.InverseTransformPoint(point);
        return Mathf.Abs(local.x) <= Fix64.Half && Mathf.Abs(local.z) <= Fix64.Half;
    }
}

[AddComponentMenu("Navigation/NavMesh Modifier Volume")]
public sealed class NavMeshModifierVolume : Behaviour
{
    public Vector3 center { get; set; }
    public Vector3 size { get; set; } = Vector3.one;
    public int area { get; set; } = 1;
    internal bool Contains(Vector3 point)
    {
        var local = transform.InverseTransformPoint(point) - center;
        return Mathf.Abs(local.x) <= size.x / 2 && Mathf.Abs(local.y) <= size.y / 2 && Mathf.Abs(local.z) <= size.z / 2;
    }
}
