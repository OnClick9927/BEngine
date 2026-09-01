using BEngine.SceneManagement;
using BEngine.Serialization;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal sealed class FixtureSceneLoader : ISceneLoader
{
    public Scene LoadScene(string sceneNameOrPath, IServiceProvider services) =>
        SceneAssetSerialization.Load(sceneNameOrPath, services);
}
