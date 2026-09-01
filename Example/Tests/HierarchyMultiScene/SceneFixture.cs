using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using BEngine.Serialization;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal sealed class SceneFixture : IDisposable
{
    public string RootPath { get; }
    public ProjectWorkspace Workspace { get; }
    public string FirstScenePath { get; }
    public string SecondScenePath { get; }

    public SceneFixture()
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"BEngineHierarchyMultiScene_{Guid.NewGuid():N}");
        Workspace = ProjectWorkspaceFactory.Create(RootPath, "Hierarchy Multi Scene");
        var sceneDirectory = Path.Combine(Workspace.AssetsPath, "Scenes");
        Directory.CreateDirectory(sceneDirectory);
        FirstScenePath = Path.Combine(sceneDirectory, "First.scene.yaml");
        SecondScenePath = Path.Combine(sceneDirectory, "Second.scene.yaml");

        var first = new Scene("First Scene");
        var firstRoot = first.CreateGameObject("First Root");
        var firstChild = first.CreateGameObject("First Child");
        firstChild.transform.SetParent(firstRoot.transform, false);
        SceneAssetSerialization.Save(first, FirstScenePath);
        first.Dispose();

        var second = new Scene("Second Scene");
        second.CreateGameObject("Second Root");
        SceneAssetSerialization.Save(second, SecondScenePath);
        second.Dispose();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
