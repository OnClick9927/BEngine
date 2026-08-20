using System.Reflection;
using BEngine.Editor;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class EditorSceneContractTests
{
    public static void Run()
    {
        var editorAssembly = typeof(EditorWindow).Assembly;
        var openSceneMode = TestAssert.RequireType(editorAssembly, "BEngine.Editor.OpenSceneMode");
        TestAssert.Require(openSceneMode.IsEnum &&
                           Enum.GetNames(openSceneMode).SequenceEqual(
                               ["Single", "Additive", "AdditiveWithoutLoading"]),
            "Editor OpenSceneMode must expose Single, Additive, and AdditiveWithoutLoading.");

        var manager = typeof(EditorSceneManager);
        TestAssert.Require(manager.GetProperty("sceneCount", BindingFlags.Public | BindingFlags.Static) is not null,
            "EditorSceneManager does not expose the number of open Scenes.");
        TestAssert.Require(manager.GetMethod("GetSceneAt", BindingFlags.Public | BindingFlags.Static,
                               binder: null, [typeof(int)], modifiers: null) is not null,
            "EditorSceneManager cannot enumerate all open Scenes.");
        TestAssert.Require(manager.GetMethod("OpenScene", BindingFlags.Public | BindingFlags.Static,
                               binder: null, [typeof(string), openSceneMode], modifiers: null) is not null,
            "EditorSceneManager does not expose Unity-style OpenScene(path, mode).");
        TestAssert.Require(manager.GetMethod("SetActiveScene", BindingFlags.Public | BindingFlags.Static,
                               binder: null, [typeof(Scene)], modifiers: null) is not null,
            "EditorSceneManager cannot switch the active Scene.");
        TestAssert.Require(manager.GetMethods(BindingFlags.Public | BindingFlags.Static).Any(method =>
            {
                var parameters = method.GetParameters();
                return method.Name == "CloseScene" && parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(Scene) &&
                       parameters[1].ParameterType == typeof(bool);
            }),
            "EditorSceneManager does not expose CloseScene(Scene, bool).");
    }
}
