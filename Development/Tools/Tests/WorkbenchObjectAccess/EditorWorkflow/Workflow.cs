using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Editor;

// Runs only in the temporary verification project. Reflection reaches the actual
// context-menu command; it does not replace an editor service or engine method.
public static class WorkbenchObjectAccessWorkflow
{
    static readonly List<string> Checks = new();
    static bool Completed;
    static string Root => Environment.GetEnvironmentVariable("BENGINE_OBJECT_ACCESS_RESULT") ?? "";

    [InitializeOnLoadMethod]
    public static void Bind()
    {
        if (Root.Length != 0) EditorApplication.update += Tick;
    }

    static void Check(bool passed, string name)
    {
        if (!passed) throw new Exception(name);
        Checks.Add(name);
    }

    static void Tick()
    {
        if (Completed || EditorApplication.isCompiling) return;
        Completed = true;
        try
        {
            var main = EditorSceneManager.GetActiveScene() ?? throw new Exception("No startup scene");
            Check(main.Id == new Guid("173f1a97-857b-4fa6-bc08-1a44edc35485") &&
                  main.path.EndsWith("Main.scene.yaml", StringComparison.OrdinalIgnoreCase),
                "Startup source loading preserves scene identity and path");
            Check(Application.isEditor && !Application.isPlaying, "The workbench remains in edit mode");
            Check(main.GetService<ISceneRuntimeFactory>() != null,
                "Public scene service lookup resolves the actual scoped runtime factory");
            var root = main.CreateGameObject("removal checks");
            var dependent = root.AddComponent<AccessDependent>();
            var required = root.GetComponent<AccessRequired>()!;
            var value = root.AddComponent<AccessValue>();
            value.Count = 42;
            var originalOrder = new List<Component>(root.components);
            var bridge = typeof(EditorApplication).Assembly.GetType("BEngine.Editor.EditorBridge", true)!;
            var host = bridge.GetProperty("Host", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var remove = host.GetType().GetMethod("RemoveComponent", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Undo.ClearAll();
            remove.Invoke(host, new object[] { new AccessValue() });
            Check(!Undo.canUndo, "An unattached component command exits without history or an exception");
            remove.Invoke(host, new object[] { required });
            Check(!Undo.canUndo && ReferenceEquals(root.GetComponent<AccessRequired>(), required),
                "The actual component command refuses required-component deletion");
            remove.Invoke(host, new object[] { value });
            Check(root.GetComponent<AccessValue>() == null && Undo.canUndo,
                "The actual component command removes an editable component and records Undo");
            Check(!EditorObjectStructureSnapshot.CanRemove(value),
                "The query disables the deleted component immediately");
            Undo.PerformUndo();
            Check(ReferenceEquals(root.GetComponent<AccessValue>(), value) && value.Count == 42 &&
                  EditorObjectStructureSnapshot.CanRemove(value),
                "Undo restores component identity, values and query state");
            Check(new List<Component>(root.components).TrueForAll(c => originalOrder.Contains(c)) &&
                  root.components.Count == originalOrder.Count, "Undo retains the original component membership");
            for (var index = 0; index < originalOrder.Count; index++)
                if (!ReferenceEquals(root.components[index], originalOrder[index]))
                    throw new Exception("Component order changed during Undo");
            Check(true, "Undo retains component order");
            Undo.PerformRedo();
            Check(root.GetComponent<AccessValue>() == null && !EditorObjectStructureSnapshot.CanRemove(value),
                "Redo removes the component without leaving a stale positive query");
            Undo.PerformUndo();
            Undo.ClearAll();
            Check(EditorSceneManager.SaveScene(main), "The edited startup scene can still be saved");
            var secondPath = Path.Combine(EditorApplication.projectPath, "Assets", "Second.scene.yaml");
            var second = EditorSceneManager.OpenScene(secondPath, OpenSceneMode.Additive)!;
            Check(second != null && second.Id == new Guid("d0c108fa-b51c-4ec0-8b40-5a34315c188b") &&
                  second.path == secondPath && EditorSceneManager.sceneCount == 2,
                "Additive source loading retains source identity, normalized path and both scenes");
            Check(second!.GetService<ISceneRuntimeFactory>() != null,
                "An additively loaded scene also resolves its scoped runtime factory");
            Check(EditorSceneManager.CloseScene(second) && EditorSceneManager.sceneCount == 1,
                "The additively loaded scene closes through the existing editor workflow");
            Check(EditorSceneManager.GetActiveScene() == main && root.GetComponent<AccessValue>() == value,
                "Closing another scene preserves the edited scene and component identity");
            Complete(true, "");
        }
        catch (Exception error) { Complete(false, error.ToString()); }
    }

    static void Complete(bool passed, string error)
    {
        File.WriteAllText(Path.Combine(Root, "Result.json"), JsonSerializer.Serialize(new
        {
            Passed = passed, Checks = Checks.Count, Details = Checks, Error = error
        }));
        EditorApplication.Exit(passed ? 0 : 1);
    }
}

public sealed class AccessValue : MonoBehaviour { public int Count; }
public sealed class AccessRequired : MonoBehaviour { }
[RequireComponent(typeof(AccessRequired))]
public sealed class AccessDependent : MonoBehaviour { }
