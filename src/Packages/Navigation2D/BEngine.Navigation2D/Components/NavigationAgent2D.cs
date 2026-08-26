namespace BEngine.Navigation2D;

[DisallowMultipleComponent]
[AddComponentMenu("Navigation 2D/Navigation Agent 2D")]
public sealed class NavigationAgent2D : Behaviour
{
    private NavigationPath2D _path = new();
    private int _cornerIndex;
    private Vector2 _destination;

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
                           _cornerIndex < _path.corners.Length;
    public bool pathPending { get; private set; }
    public bool isStopped { get; set; }
    public NavigationPathStatus pathStatus => _path.status;

    public Fix64 remainingDistance
    {
        get
        {
            if (!hasPath) return Fix64.Zero;
            var corners = _path.corners;
            var total = Vector2.Distance(transform.position, corners[_cornerIndex]);
            for (var index = _cornerIndex; index < corners.Length - 1; index++)
                total += Vector2.Distance(corners[index], corners[index + 1]);
            return total;
        }
    }

    public bool SetDestination(Vector2 target)
    {
        _destination = target;
        pathPending = true;
        var path = new NavigationPath2D();
        var result = Navigation2D.CalculatePath(transform.position, target, areaMask, path);
        pathPending = false;
        if (!result)
        {
            ResetPath();
            return false;
        }
        _path = path;
        _cornerIndex = Math.Min(1, _path.corners.Length - 1);
        return true;
    }

    public bool SetPath(NavigationPath2D path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.status == NavigationPathStatus.PathInvalid) return false;
        _path = path;
        _cornerIndex = Math.Min(1, path.corners.Length - 1);
        return true;
    }

    public void ResetPath()
    {
        _path = new NavigationPath2D();
        _cornerIndex = 0;
        velocity = Vector2.zero;
        desiredVelocity = Vector2.zero;
    }

    public bool Warp(Vector2 newPosition)
    {
        transform.position = newPosition;
        ResetPath();
        return true;
    }

    internal void Tick(Fix64 deltaTime)
    {
        if (!enabled || isStopped || !hasPath)
        {
            desiredVelocity = Vector2.zero;
            velocity = Vector2.zero;
            return;
        }
        var corners = _path.corners;
        var target = corners[_cornerIndex];
        var delta = target - transform.position;
        if (delta.magnitude <= Mathf.Max(stoppingDistance, Fix64.Parse("0.05")))
        {
            _cornerIndex++;
            if (_cornerIndex >= corners.Length)
            {
                ResetPath();
                return;
            }
            target = corners[_cornerIndex];
            delta = target - transform.position;
        }
        desiredVelocity = delta.normalized * speed;
        velocity = MoveTowards(velocity, desiredVelocity, acceleration * deltaTime);
        if (updatePosition) transform.position += velocity * deltaTime;
        if (updateRotation && velocity.sqrMagnitude > Fix64.Epsilon)
            transform.rotation = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
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
