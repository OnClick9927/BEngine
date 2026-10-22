namespace BEngine.Navigation2D;

[DisallowMultipleComponent]
[AddComponentMenu("Navigation 2D/Navigation Agent 2D")]
public sealed class NavigationAgent2D : Behaviour
{
    private static readonly Fix64 CornerReachEpsilon = Fix64.Parse("0.01");
    private NavigationPath2D _path = new();
    private int _cornerIndex;
    private Vector2 _destination;
    private Vector2 _position;
    private bool _positionInitialized;
    private bool _hasDestination;
    private long _pathRevision;
    public Vector2 nextPosition
    {
        get { if (!_positionInitialized) { _position = transform.position; _positionInitialized = true; } return _position; }
        set { _position = value; _positionInitialized = true; }
    }
    internal Vector2 PeekAvoidancePosition() => _positionInitialized ? _position : transform.position;

    public Fix64 radius { get; set; } = Fix64.Half;
    public Fix64 speed { get; set; } = Fix64.Parse("3.5");
    public Fix64 acceleration { get; set; } = 8;
    public Fix64 angularSpeed { get; set; } = 360;
    public Fix64 stoppingDistance { get; set; } = Fix64.Parse("0.1");
    public bool autoBraking { get; set; } = true;
    public bool autoRepath { get; set; } = true;
    public bool updatePosition { get; set; } = true;
    public bool updateRotation { get; set; } = true;
    public ulong areaMask { get; set; } = ulong.MaxValue;
    public int agentTypeId { get; set; }
    public ObstacleAvoidanceType obstacleAvoidanceType { get; set; } =
        ObstacleAvoidanceType.HighQualityObstacleAvoidance;
    public Vector2 velocity { get; set; }
    public Vector2 desiredVelocity { get; private set; }
    public Vector2 destination { get => _destination; set => SetDestination(value); }
    public bool hasPath => _path.status != NavigationPathStatus.PathInvalid &&
                           _cornerIndex >= 0 && _cornerIndex < _path.CornersUnchecked.Length;
    public bool pathPending { get; private set; }
    public bool isStopped { get; set; }
    public NavigationPathStatus pathStatus => _path.status;

    public Fix64 remainingDistance
    {
        get
        {
            if (!hasPath) return Fix64.Zero;
            var corners = _path.CornersUnchecked;
            var total = Vector2.Distance(nextPosition, corners[_cornerIndex]);
            for (var index = _cornerIndex; index < corners.Length - 1; index++)
                total += Vector2.Distance(corners[index], corners[index + 1]);
            return total;
        }
    }

    public bool SetDestination(Vector2 target)
    {
        _destination = target;
        _hasDestination = true;
        _pathRevision = Navigation2D.Revision;
        pathPending = true;
        var path = new NavigationPath2D();
        bool result;
        try { result = Navigation2D.CalculatePath(nextPosition, target, areaMask, path, agentTypeId); }
        finally { pathPending = false; }
        if (!result)
        {
            ResetPath();
            _hasDestination = true;
            return false;
        }
        _path = path;
        _cornerIndex = Math.Min(1, _path.CornersUnchecked.Length - 1);
        return true;
    }

    public bool SetPath(NavigationPath2D path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.status == NavigationPathStatus.PathInvalid || path.CornersUnchecked.Length == 0) return false;
        _path = new NavigationPath2D { corners = path.corners, status = path.status };
        _cornerIndex = Math.Min(1, _path.CornersUnchecked.Length - 1);
        _destination = _path.CornersUnchecked[^1];
        _hasDestination = true;
        _pathRevision = Navigation2D.Revision;
        return true;
    }

    public void ResetPath()
    {
        _path = new NavigationPath2D();
        _cornerIndex = 0;
        velocity = Vector2.zero;
        desiredVelocity = Vector2.zero;
        _hasDestination = false;
    }

    public bool Warp(Vector2 newPosition)
    {
        if (!Navigation2D.SamplePosition(newPosition, out var hit,
                Mathf.Max(radius, Fix64.Epsilon), areaMask, agentTypeId)) return false;
        nextPosition = hit.position;
        if (updatePosition) transform.position = nextPosition;
        ResetPath();
        return true;
    }

    internal void Tick(Fix64 deltaTime, NavigationAvoidanceBatch? batch = null, int slot = -1)
    {
        if (updatePosition) nextPosition = transform.position;
        if (enabled && autoRepath && _hasDestination && _pathRevision != Navigation2D.Revision)
            SetDestination(_destination);
        if (!enabled || isStopped || !hasPath)
        {
            desiredVelocity = Vector2.zero;
            velocity = Vector2.zero;
            return;
        }
        if (deltaTime <= 0) return;
        var corners = _path.CornersUnchecked;
        var target = corners[_cornerIndex];
        var delta = target - nextPosition;
        while (delta.magnitude <= (_cornerIndex == corners.Length - 1
                   ? Mathf.Max(stoppingDistance, Fix64.Epsilon) : CornerReachEpsilon))
        {
            _cornerIndex++;
            if (_cornerIndex >= corners.Length)
            {
                ResetPath();
                return;
            }
            target = corners[_cornerIndex];
            delta = target - nextPosition;
        }
        var desiredSpeed = Mathf.Max(0, speed);
        if (autoBraking && acceleration > 0)
            desiredSpeed = Mathf.Min(desiredSpeed, Mathf.Sqrt(2 * acceleration *
                Mathf.Max(0, remainingDistance - stoppingDistance)));
        desiredVelocity = delta.normalized * desiredSpeed;
        desiredVelocity = AvoidOtherAgents(desiredVelocity, desiredSpeed, batch, slot);
        velocity = MoveTowards(velocity, desiredVelocity, Mathf.Max(0, acceleration) * deltaTime);
        var movement = velocity * deltaTime;
        if (movement.magnitude > delta.magnitude) movement = delta;
        nextPosition += movement;
        if (updatePosition) transform.position = nextPosition;
        if (updateRotation && velocity.sqrMagnitude > Fix64.Epsilon)
        {
            var angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
            var difference = Mathf.Repeat(angle - transform.rotation + 180, 360) - 180;
            var maximum = Mathf.Max(0, angularSpeed) * deltaTime;
            transform.rotation += Mathf.Clamp(difference, -maximum, maximum);
        }
    }

    private Vector2 AvoidOtherAgents(Vector2 desired, Fix64 desiredSpeed, NavigationAvoidanceBatch? batch, int slot)
    {
        if (obstacleAvoidanceType == ObstacleAvoidanceType.NoObstacleAvoidance || gameObject.scene is not { } scene)
            return desired;
        var adjustment = Vector2.zero;
        if (batch is not null)
            return (desired + batch.Adjustment(slot, nextPosition, radius, desiredSpeed)).normalized * desiredSpeed;
        foreach (var other in scene.QueryComponents<NavigationAgent2D>())
        {
            if (ReferenceEquals(other, this) || !other.enabled || !other.gameObject.activeInHierarchy) continue;
            var separation = nextPosition - other.nextPosition;
            var distance = separation.magnitude;
            var clearance = Mathf.Max(0, radius) + Mathf.Max(0, other.radius);
            if (distance > Fix64.Epsilon && distance < clearance)
                adjustment += separation / distance * (Fix64.One - distance / clearance) * desiredSpeed;
        }
        return (desired + adjustment).normalized * desiredSpeed;
    }

    private static Vector2 MoveTowards(Vector2 current, Vector2 target, Fix64 maximumDelta)
    {
        var delta = target - current;
        var distance = delta.magnitude;
        return distance <= maximumDelta || distance <= Fix64.Epsilon
            ? target
            : current + delta / distance * maximumDelta;
    }
}
