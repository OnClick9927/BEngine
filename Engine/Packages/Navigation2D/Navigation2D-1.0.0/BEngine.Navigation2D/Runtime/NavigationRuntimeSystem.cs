namespace BEngine.Navigation2D;

public sealed class NavigationRuntimeSystem : ISceneRuntimeSystem
{
    private readonly List<NavigationAgent2D> _agents = [];
    private readonly NavigationAvoidanceBatch _avoidance = new();
    private bool _updating;
    public long lastAvoidanceCandidateCount { get; private set; }
    public int order => 100;
    public string packageId => "com.bengine.navigation2d";
    public void Start(Scene scene) => Navigation2D.SetScene(scene);
    public void Update(Scene scene, Fix64 deltaTime)
    {
        if (_updating) throw new InvalidOperationException("NavigationRuntimeSystem does not support reentrant updates.");
        Navigation2D.SetScene(scene);
        _updating = true; lastAvoidanceCandidateCount = 0;
        try
        {
            scene.GetComponents(_agents);
            _avoidance.Begin(_agents);
            for (var index = 0; index < _agents.Count; index++)
            {
                var agent = _agents[index];
                if (agent.isDestroyed || !agent.enabled || !agent.gameObject.activeInHierarchy) continue;
                agent.Tick(deltaTime, _avoidance, index);
                _avoidance.Move(index, agent.PeekAvoidancePosition());
            }
            lastAvoidanceCandidateCount = _avoidance.CandidateCount;
        }
        finally { _avoidance.Clear(); _agents.Clear(); _updating = false; }
    }
    public void Stop(Scene scene) { _avoidance.Clear(); _agents.Clear(); Navigation2D.SetScene(null); }
}
