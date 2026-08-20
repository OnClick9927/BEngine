using BEngine.Editor;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class EditorSceneLifecycleTests
{
    public static void Run(SceneFixture fixture)
    {
        using var harness = new EditorApplicationHarness(fixture);
        var opened = new List<(Scene Scene, OpenSceneMode Mode)>();
        var closed = new List<Scene>();
        var activeChanges = new List<(Scene? Previous, Scene Next)>();
        EditorSceneManager.sceneOpened += OnOpened;
        EditorSceneManager.sceneClosed += OnClosed;
        EditorSceneManager.activeSceneChangedInEditMode += OnActiveChanged;
        try
        {
            var first = EditorSceneManager.GetSceneAt(0);
            TestAssert.Require(EditorSceneManager.sceneCount == 1 &&
                               ReferenceEquals(EditorSceneManager.activeScene, first),
                "The initial editor Scene was not registered as active.");

            var second = EditorSceneManager.OpenScene(fixture.SecondScenePath, OpenSceneMode.Additive) ??
                         throw new InvalidOperationException("Additive editor Scene loading returned null.");
            TestAssert.Require(EditorSceneManager.sceneCount == 2 &&
                               ReferenceEquals(EditorSceneManager.GetSceneAt(0), first) &&
                               ReferenceEquals(EditorSceneManager.GetSceneAt(1), second),
                "Additive editor loading did not preserve stable open-Scene order.");
            TestAssert.Require(ReferenceEquals(EditorSceneManager.activeScene, second) &&
                               first.gameObjects.All(item => ReferenceEquals(item.scene, first)) &&
                               second.gameObjects.All(item => ReferenceEquals(item.scene, second)),
                "An additively opened Scene was not active or does not own its hierarchy.");
            TestAssert.Require(EditorSceneManager.SetActiveScene(first) &&
                               ReferenceEquals(EditorSceneManager.activeScene, first),
                "EditorSceneManager.SetActiveScene did not switch to the first loaded Scene.");
            TestAssert.Require(EditorSceneManager.CloseScene(second, removeScene: true) &&
                               EditorSceneManager.sceneCount == 1,
                "Closing an additive Scene did not remove it from the editor Scene list.");

            var unloaded = EditorSceneManager.OpenScene(
                fixture.SecondScenePath, OpenSceneMode.AdditiveWithoutLoading) ??
                           throw new InvalidOperationException("AdditiveWithoutLoading returned null.");
            TestAssert.Require(EditorSceneManager.sceneCount == 2 && !unloaded.isLoaded &&
                               unloaded.rootCount == 0 && ReferenceEquals(EditorSceneManager.activeScene, first),
                "AdditiveWithoutLoading loaded contents or replaced the active Scene.");
            TestAssert.Require(!EditorSceneManager.SetActiveScene(unloaded),
                "An unloaded placeholder Scene was accepted as active.");
            TestAssert.Require(EditorSceneManager.CloseScene(unloaded, removeScene: true),
                "An unloaded placeholder Scene could not be closed.");

            var loadedAgain = EditorSceneManager.OpenScene(fixture.SecondScenePath, OpenSceneMode.Additive) ??
                              throw new InvalidOperationException("Second additive load returned null.");
            var single = EditorSceneManager.OpenScene(fixture.SecondScenePath, OpenSceneMode.Single) ??
                         throw new InvalidOperationException("Single editor Scene loading returned null.");
            TestAssert.Require(ReferenceEquals(single, loadedAgain) && EditorSceneManager.sceneCount == 1 &&
                               ReferenceEquals(EditorSceneManager.activeScene, loadedAgain),
                "Single editor loading did not leave exactly the requested Scene active.");
            TestAssert.Require(!first.isLoaded && !first.world.IsCreated,
                "A Scene replaced by Single mode still reports itself as loaded.");

            TestAssert.Require(opened.Any(item => item.Mode == OpenSceneMode.Additive) &&
                               opened.Any(item => item.Mode == OpenSceneMode.AdditiveWithoutLoading),
                "Editor sceneOpened did not describe additive opening modes.");
            TestAssert.Require(closed.Contains(second) && closed.Contains(unloaded) && closed.Contains(first),
                "Editor sceneClosed missed a removed loaded or placeholder Scene.");
            TestAssert.Require(activeChanges.Any(item => ReferenceEquals(item.Previous, second) &&
                                                         ReferenceEquals(item.Next, first)),
                "Editor active-Scene changes were not reported.");
        }
        finally
        {
            EditorSceneManager.sceneOpened -= OnOpened;
            EditorSceneManager.sceneClosed -= OnClosed;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveChanged;
        }

        void OnOpened(Scene scene, OpenSceneMode mode) => opened.Add((scene, mode));
        void OnClosed(Scene scene) => closed.Add(scene);
        void OnActiveChanged(Scene? previous, Scene next) => activeChanges.Add((previous, next));
    }
}
