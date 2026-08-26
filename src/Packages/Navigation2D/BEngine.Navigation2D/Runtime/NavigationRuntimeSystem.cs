namespace BEngine.Navigation2D;

public sealed class NavigationRuntimeSystem : ISceneRuntimeSystem
{
    public int order => 100;
    public string packageId => "com.bengine.navigation2d";
    public void Start(Scene scene) => Navigation2D.SetScene(scene);
    public void Update(Scene scene, Fix64 deltaTime)
    {
        Navigation2D.SetScene(scene);
        foreach (var agent in scene.QueryComponents<NavigationAgent2D>())
            if (agent.enabled && agent.gameObject.activeInHierarchy) agent.Tick(deltaTime);
    }
    public void Stop(Scene scene) => Navigation2D.SetScene(null);
}
