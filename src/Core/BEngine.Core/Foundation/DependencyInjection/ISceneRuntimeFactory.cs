namespace BEngine.DependencyInjection;

public interface ISceneRuntimeFactory
{
    SceneRuntime Create(Scene scene);
}
