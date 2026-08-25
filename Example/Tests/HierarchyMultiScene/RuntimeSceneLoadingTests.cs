using BEngine.Documents;
using BEngine.SceneManagement;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class RuntimeSceneLoadingTests
{
    public static void Run(SceneFixture fixture)
    {
        TestAssert.Require(Enum.GetNames<LoadSceneMode>().SequenceEqual(["Single", "Additive"]),
            "Runtime scene loading modes diverged from Unity's Single/Additive contract.");

        var services = new RuntimeSceneServiceProvider();
        var manager = services.SceneManager;
        var first = manager.LoadScene(fixture.FirstScenePath);
        var firstRoot = first.Find("First Root") ??
                        throw new InvalidOperationException("First scene did not load its root GameObject.");
        var firstChild = first.Find("First Child") ??
                         throw new InvalidOperationException("First scene did not load its child GameObject.");
        TestAssert.Require(manager.SceneCount == 1 && ReferenceEquals(manager.ActiveScene, first),
            "Single loading did not establish the only active Scene.");
        TestAssert.Require(first.gameObjects.All(item => ReferenceEquals(item.scene, first)),
            "Loaded GameObjects do not report their owning Scene.");

        var second = manager.LoadScene(fixture.SecondScenePath, LoadSceneMode.Additive);
        TestAssert.Require(manager.SceneCount == 2 &&
                           ReferenceEquals(manager.LoadedScenes[0], first) &&
                           ReferenceEquals(manager.LoadedScenes[1], second),
            "Additive loading did not preserve the first Scene in stable order.");
        TestAssert.Require(ReferenceEquals(manager.ActiveScene, second) &&
                           second.gameObjects.All(item => ReferenceEquals(item.scene, second)),
            "The additively loaded Scene was not made active or does not own its objects.");
        TestAssert.Require(manager.SetActiveScene(first) && ReferenceEquals(manager.ActiveScene, first),
            "SetActiveScene did not switch to another loaded Scene.");

        BObject.DontDestroyOnLoad(firstChild);
        TestAssert.Require(firstRoot.isDontDestroyOnLoad && firstChild.isDontDestroyOnLoad,
            "DontDestroyOnLoad on a child did not mark the hierarchy root.");
        var transientDocument = Document.FromBObject<SceneDocument>(first).ToYaml();
        TestAssert.Require(!transientDocument.Contains("DontDestroyOnLoad", StringComparison.OrdinalIgnoreCase),
            "DontDestroyOnLoad leaked from runtime state into SceneDocument serialization.");
        var replacement = manager.LoadScene(fixture.SecondScenePath, LoadSceneMode.Single);
        TestAssert.Require(manager.SceneCount == 1 && ReferenceEquals(manager.ActiveScene, replacement),
            "A subsequent Single load did not replace all ordinary loaded Scenes.");
        TestAssert.Require(!first.isLoaded && !second.isLoaded,
            "Single loading left replaced Scenes marked as loaded.");
        TestAssert.Require(ReferenceEquals(firstRoot.scene, replacement) &&
                           ReferenceEquals(firstChild.scene, replacement) &&
                           replacement.Find(firstRoot.Id) is not null &&
                           replacement.Find(firstChild.Id) is not null,
            "DontDestroyOnLoad did not preserve and move the complete GameObject hierarchy.");
        TestAssert.Require(replacement.gameObjects.Contains(firstRoot) &&
                           replacement.gameObjects.Contains(firstChild) &&
                           !first.gameObjects.Contains(firstRoot) && !first.gameObjects.Contains(firstChild),
            "Preserved GameObjects were not moved between the managed Scene collections.");
        TestAssert.Require(manager.UnregisterScene(replacement) && manager.SceneCount == 0 &&
                           replacement.isLoaded && replacement.isCreated &&
                           replacement.gameObjects.Contains(firstRoot),
            "Non-destructive Scene unregistration disposed the Scene or its managed objects.");
        replacement.Dispose();
    }
}
