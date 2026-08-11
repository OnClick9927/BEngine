namespace BEngine.Navigation;

public enum NavMeshPathStatus { PathComplete, PathPartial, PathInvalid }
public enum ObstacleAvoidanceType { NoObstacleAvoidance, LowQualityObstacleAvoidance, MedQualityObstacleAvoidance, GoodQualityObstacleAvoidance, HighQualityObstacleAvoidance }
public enum NavMeshCollectGeometry { RenderMeshes, PhysicsColliders }
public enum CollectObjects { All, Volume, Children }
public enum NavMeshObstacleShape { Capsule, Box }

public sealed class NavMeshPath
{
    private BEngine.Vector3[] _corners = [];
    public BEngine.Vector3[] corners { get => [.. _corners]; internal set => _corners = value ?? []; }
    public NavMeshPathStatus status { get; internal set; } = NavMeshPathStatus.PathInvalid;
    public void ClearCorners() { _corners = []; status = NavMeshPathStatus.PathInvalid; }
}

public readonly struct NavMeshHit
{
    public BEngine.Vector3 position { get; init; }
    public BEngine.Fix64 distance { get; init; }
    public int mask { get; init; }
}
