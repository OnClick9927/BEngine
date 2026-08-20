using BEngine.SceneManagement;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal sealed class RuntimeSceneServiceProvider : IServiceProvider
{
    private readonly ISceneLoader _loader = new FixtureSceneLoader();

    public IRuntimeSceneManager SceneManager { get; }

    public RuntimeSceneServiceProvider() => SceneManager = new RuntimeSceneManager(this);

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(ISceneLoader)) return _loader;
        if (serviceType == typeof(IRuntimeSceneManager) || serviceType == typeof(RuntimeSceneManager))
            return SceneManager;
        return null;
    }
}
