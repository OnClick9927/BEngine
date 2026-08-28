using BEngine.Editor;

namespace BEngine.ExampleTests.InspectorComponentActions;

internal static class StructuralUndoTests
{
    internal static void Run()
    {
        Undo.ClearAll();
        var target = new GameObject("Structural Target");
        var component = ObjectFactory.AddComponent<InspectorActionProbe>(target);
        TestAssert.Require(ReferenceEquals(target.GetComponent<InspectorActionProbe>(), component),
            "ObjectFactory.AddComponent did not attach the new component.");
        Undo.PerformUndo();
        TestAssert.Require(target.GetComponent<InspectorActionProbe>() is null,
            "Undo of Add Component left the component attached.");
        Undo.PerformRedo();
        TestAssert.Require(ReferenceEquals(target.GetComponent<InspectorActionProbe>(), component),
            "Redo of Add Component did not restore the exact component instance.");

        Undo.ClearAll();
        Undo.DestroyObjectImmediate(component);
        TestAssert.Require(target.GetComponent<InspectorActionProbe>() is null,
            "DestroyObjectImmediate did not structurally remove the component.");
        Undo.PerformUndo();
        TestAssert.Require(ReferenceEquals(target.GetComponent<InspectorActionProbe>(), component),
            "Undo of Remove Component did not restore the exact component instance.");
        Undo.PerformRedo();
        TestAssert.Require(target.GetComponent<InspectorActionProbe>() is null,
            "Redo of Remove Component left the component attached.");

        VerifyHierarchyAndSiblingUndo();
        VerifyFullHierarchyStructuralUndo();
        VerifyGroupsAndTargetClearing();
        VerifySameGroupAndName();
        VerifyInteractionGroupBoundaries();
        VerifyRejectedDestroyDoesNotCreateHistory();
        VerifyFileUndoDoesNotAffectScenes();
        Undo.ClearAll();
    }

    private static void VerifyHierarchyAndSiblingUndo()
    {
        var root = new GameObject("Root Before");
        var child = new GameObject("Child Before");
        var sibling = new GameObject("Sibling");
        child.transform.SetParent(root.transform, false);
        sibling.transform.SetParent(root.transform, false);
        var probe = ObjectFactory.AddComponent<InspectorActionProbe>(child);
        Undo.ClearAll();

        Undo.RegisterFullObjectHierarchyUndo(root, "Edit Hierarchy");
        child.name = "Child After";
        probe.enabled = false;
        Undo.PerformUndo();
        TestAssert.Require(child.name == "Child Before" && probe.enabled,
            "Full hierarchy Undo did not restore a descendant GameObject and component.");
        Undo.PerformRedo();
        TestAssert.Require(child.name == "Child After" && !probe.enabled,
            "Full hierarchy Redo did not restore descendant state.");

        Undo.ClearAll();
        Undo.SetSiblingIndex(sibling.transform, 0, "Reorder Children");
        TestAssert.Require(ReferenceEquals(root.transform.GetChild(0), sibling.transform),
            "SetSiblingIndex did not apply the requested order.");
        Undo.PerformUndo();
        TestAssert.Require(ReferenceEquals(root.transform.GetChild(0), child.transform),
            "Undo did not restore the previous sibling order.");
        Undo.PerformRedo();
        TestAssert.Require(ReferenceEquals(root.transform.GetChild(0), sibling.transform),
            "Redo did not restore the reordered siblings.");
    }

    private static void VerifyGroupsAndTargetClearing()
    {
        var first = new GameObject("Zero");
        var second = new GameObject("Second Before");
        Undo.ClearAll();
        var group = Undo.GetCurrentGroup();
        Undo.RecordObject(first, "First Rename");
        first.name = "One";
        Undo.IncrementCurrentGroup();
        Undo.RecordObject(first, "Second Rename");
        first.name = "Two";
        Undo.CollapseUndoOperations(group);
        Undo.PerformUndo();
        TestAssert.Require(first.name == "Zero",
            "Collapsed Undo operations did not execute as one ordered step.");
        Undo.PerformRedo();
        TestAssert.Require(first.name == "Two",
            "Collapsed Redo operations did not preserve chronological order.");

        Undo.ClearAll();
        Undo.RecordObject(first, "First Target");
        first.name = "First Changed";
        Undo.RecordObject(second, "Second Target");
        second.name = "Second Changed";
        Undo.ClearUndo(second);
        Undo.PerformUndo();
        TestAssert.Require(first.name == "Two" && second.name == "Second Changed" && !Undo.canUndo,
            "ClearUndo did not remove only the requested target's history.");

        Undo.RecordObject(first, "Revert Without Redo");
        first.name = "Temporary";
        Undo.RevertAllInCurrentGroup();
        TestAssert.Require(first.name == "Two" && !Undo.canRedo,
            "RevertAllInCurrentGroup retained a Redo entry or failed to restore state.");
    }

    private static void VerifyFullHierarchyStructuralUndo()
    {
        using var scene = new Scene("Full Hierarchy Undo");
        var root = scene.CreateGameObject("Root Before");
        var removedChild = scene.CreateGameObject("Removed Child");
        var removedGrandchild = scene.CreateGameObject("Removed Grandchild");
        var survivingChild = scene.CreateGameObject("Surviving Before");
        removedChild.transform.SetParent(root.transform, false);
        removedGrandchild.transform.SetParent(removedChild.transform, false);
        survivingChild.transform.SetParent(root.transform, false);
        var first = root.AddComponent<InspectorActionProbe>();
        first.probeValue = 10;
        var second = root.AddComponent<InspectorActionProbe>();
        second.probeValue = 20;

        Undo.ClearAll();
        Undo.RegisterFullObjectHierarchyUndo(root, "Structural Hierarchy Edit");
        root.name = "Root After";
        survivingChild.name = "Surviving After";
        second.probeValue = 200;
        TestAssert.Require(scene.Destroy(removedChild),
            "Test setup could not remove the original child hierarchy.");
        var addedChild = scene.CreateGameObject("Added Child");
        addedChild.transform.SetParent(root.transform, false);
        survivingChild.transform.SetParent(addedChild.transform, false);
        TestAssert.Require(root.RemoveComponent(first),
            "Test setup could not remove the original component.");
        var addedComponent = root.AddComponent<InspectorActionProbe>();
        addedComponent.probeValue = 300;

        Undo.PerformUndo();
        TestAssert.Require(root.name == "Root Before" && survivingChild.name == "Surviving Before" &&
                           second.probeValue == 20,
            "Full hierarchy Undo did not restore GameObject and component values.");
        TestAssert.Require(ReferenceEquals(removedChild.scene, scene) &&
                           ReferenceEquals(removedGrandchild.scene, scene) && addedChild.scene is null,
            "Full hierarchy Undo did not restore deleted objects or remove newly added objects.");
        TestAssert.Require(root.transform.childCount == 2 &&
                           ReferenceEquals(root.transform.GetChild(0), removedChild.transform) &&
                           ReferenceEquals(root.transform.GetChild(1), survivingChild.transform) &&
                           ReferenceEquals(removedGrandchild.transform.parent, removedChild.transform),
            "Full hierarchy Undo did not restore parent and sibling order.");
        TestAssert.Require(root.components.Count == 3 &&
                           ReferenceEquals(root.components[1], first) &&
                           ReferenceEquals(root.components[2], second) &&
                           !root.components.Contains(addedComponent),
            "Full hierarchy Undo did not restore exact component instances and order.");

        Undo.PerformRedo();
        TestAssert.Require(root.name == "Root After" && survivingChild.name == "Surviving After" &&
                           second.probeValue == 200 && addedComponent.probeValue == 300,
            "Full hierarchy Redo did not restore changed values.");
        TestAssert.Require(removedChild.scene is null && removedGrandchild.scene is null &&
                           ReferenceEquals(addedChild.scene, scene) &&
                           ReferenceEquals(addedChild.transform.parent, root.transform) &&
                           ReferenceEquals(survivingChild.transform.parent, addedChild.transform),
            "Full hierarchy Redo did not restore added, deleted, or reparented objects.");
        TestAssert.Require(root.components.Count == 3 &&
                           ReferenceEquals(root.components[1], second) &&
                           ReferenceEquals(root.components[2], addedComponent) &&
                           !root.components.Contains(first),
            "Full hierarchy Redo did not restore the changed component order.");
        Undo.ClearAll();
    }

    private static void VerifySameGroupAndName()
    {
        var first = new GameObject("First Before");
        var second = new GameObject("Second Before");
        Undo.ClearAll();
        TestAssert.Require(Undo.GetCurrentGroupName().Length == 0,
            "An empty Undo group reported a stale name.");
        Undo.RecordObject(first, "Rename First");
        first.name = "First After";
        Undo.RecordObject(second, "Rename Second");
        second.name = "Second After";
        Undo.SetCurrentGroupName("Batch Rename");
        TestAssert.Require(Undo.GetCurrentGroupName() == "Batch Rename",
            "SetCurrentGroupName did not rename the active group.");

        Undo.PerformUndo();
        TestAssert.Require(first.name == "First Before" && second.name == "Second Before" &&
                           !Undo.canUndo && Undo.canRedo,
            "One Undo did not restore every consecutive operation in the same group.");
        Undo.PerformRedo();
        TestAssert.Require(first.name == "First After" && second.name == "Second After" &&
                           Undo.canUndo && !Undo.canRedo,
            "One Redo did not replay every consecutive operation in the same group.");
    }

    private static void VerifyInteractionGroupBoundaries()
    {
        var target = new GameObject("Before Drag");
        Undo.ClearAll();
        RunGuiEvent(EventType.MouseDown, () =>
        {
            Undo.RecordObject(target, "Drag Rename");
            target.name = "Drag Down";
        });
        RunGuiEvent(EventType.MouseDrag, () =>
        {
            Undo.RecordObject(target, "Drag Rename");
            target.name = "Drag One";
        });
        RunGuiEvent(EventType.MouseDrag, () =>
        {
            Undo.RecordObject(target, "Drag Rename");
            target.name = "Drag Final";
        });
        RunGuiEvent(EventType.MouseUp, static () => Event.current.Use());

        RunGuiEvent(EventType.MouseDown, () =>
        {
            Undo.RecordObject(target, "Click Rename");
            target.name = "After Click";
        });
        RunGuiEvent(EventType.MouseUp, static () => Event.current.Use());

        Undo.PerformUndo();
        TestAssert.Require(target.name == "Drag Final",
            "The next click was not separated from the preceding drag Undo group.");
        Undo.PerformUndo();
        TestAssert.Require(target.name == "Before Drag" && !Undo.canUndo,
            "Continuous MouseDrag events were split into multiple Undo groups.");
    }

    private static void VerifyRejectedDestroyDoesNotCreateHistory()
    {
        var target = new GameObject("Required Destroy Guard");
        target.AddComponent<RequiredOwnerProbe>();
        var required = target.GetComponent<RequiredLeafProbe>() ??
                       throw new InvalidOperationException("Required component was not created.");
        Undo.ClearAll();
        Undo.DestroyObjectImmediate(required);
        TestAssert.Require(ReferenceEquals(target.GetComponent<RequiredLeafProbe>(), required) &&
                           !Undo.canUndo,
            "A rejected component deletion created a ghost Undo record or changed the object.");
    }

    private static void VerifyFileUndoDoesNotAffectScenes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"BEngineUndo-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "Before");
            Undo.ClearAll();
            var register = typeof(Undo).GetMethod("RegisterFileUndo",
                               System.Reflection.BindingFlags.Static |
                               System.Reflection.BindingFlags.NonPublic) ??
                           throw new MissingMethodException(typeof(Undo).FullName, "RegisterFileUndo");
            register.Invoke(null, [path, "Assets/BEngineUndo.txt", "File Edit"]);
            var undoStack = typeof(Undo).GetField("UndoStack",
                                System.Reflection.BindingFlags.Static |
                                System.Reflection.BindingFlags.NonPublic)?.GetValue(null) ??
                            throw new MissingFieldException(typeof(Undo).FullName, "UndoStack");
            var operation = undoStack.GetType().GetMethod("Peek")?.Invoke(undoStack, null) ??
                            throw new InvalidOperationException("Could not inspect the file Undo operation.");
            var affectsScene = (bool)(operation.GetType().GetProperty("AffectsScene")?.GetValue(operation) ?? true);
            TestAssert.Require(!affectsScene,
                "A file Undo operation was registered as scene-affecting.");
        }
        finally
        {
            Undo.ClearAll();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void RunGuiEvent(EventType type, Action action)
    {
        var begin = typeof(GUI).GetMethod("BeginFrame",
                        System.Reflection.BindingFlags.Static |
                        System.Reflection.BindingFlags.NonPublic) ??
                    throw new MissingMethodException(typeof(GUI).FullName, "BeginFrame");
        var end = typeof(GUI).GetMethod("EndFrame",
                      System.Reflection.BindingFlags.Static |
                      System.Reflection.BindingFlags.NonPublic) ??
                  throw new MissingMethodException(typeof(GUI).FullName, "EndFrame");
        var commands = Activator.CreateInstance(begin.GetParameters()[3].ParameterType) ??
                       throw new InvalidOperationException("Could not create the IMGUI command list.");
        begin.Invoke(null, [new Event(type), 160, 80, commands]);
        try { action(); }
        finally { end.Invoke(null, null); }
    }
}
