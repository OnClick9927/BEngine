namespace BEngine.DependencyInjection;

public sealed class SceneRuntimeFactory : ISceneRuntimeFactory
{
    public SceneRuntime Create(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return new SceneRuntime(scene);
    }
}
