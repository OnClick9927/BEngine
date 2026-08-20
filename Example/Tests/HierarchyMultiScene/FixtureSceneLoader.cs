using BEngine.Documents;
using BEngine.SceneManagement;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal sealed class FixtureSceneLoader : ISceneLoader
{
    public Scene LoadScene(string sceneNameOrPath, IServiceProvider services) =>
        Document.LoadBObject<SceneDocument, Scene>(sceneNameOrPath, services);
}
