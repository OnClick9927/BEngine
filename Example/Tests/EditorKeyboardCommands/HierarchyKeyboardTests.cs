using BEngine.Editor;

namespace BEngine.ExampleTests.EditorKeyboardCommands;

internal static class HierarchyKeyboardTests
{
    internal static void Run()
    {
        VerifyRenameAndUndoRedo();
        VerifyCopyDuplicatePaste();
        VerifyDelete();
    }

    private static void VerifyRenameAndUndoRedo()
    {
        using var fixture = new KeyboardCommandFixture();
        using var harness = new EditorKeyboardHarness(fixture);
        var root = harness.Root;
        harness.Select(root);
        var rename = harness.SendHierarchyKey(KeyCode.F2);
        TestAssert.Require(rename.type == EventType.Used && harness.HierarchyRenamingId == root.Id,
            "Hierarchy F2 did not enter rename mode for the selected GameObject.");
        harness.SetHierarchyRenameValue("Renamed By F2");
        harness.SendHierarchyKey(KeyCode.Return);
        TestAssert.Require(root.name == "Renamed By F2", "Hierarchy rename did not commit with Return.");

        var undo = harness.SendHierarchyKey(KeyCode.Z, EventModifiers.Control);
        TestAssert.Require(undo.type == EventType.Used && root.name == "Keyboard Root",
            "Ctrl+Z did not undo the focused Hierarchy edit.");
        var redo = harness.SendHierarchyKey(KeyCode.Y, EventModifiers.Control);
        TestAssert.Require(redo.type == EventType.Used && root.name == "Renamed By F2",
            "Ctrl+Y did not redo the focused Hierarchy edit.");
    }

    private static void VerifyCopyDuplicatePaste()
    {
        using var fixture = new KeyboardCommandFixture();
        using var harness = new EditorKeyboardHarness(fixture);
        var source = harness.Root;
        harness.Select(source);
        var beforeDuplicate = harness.Scene.gameObjects.Count;
        var duplicate = harness.SendHierarchyKey(KeyCode.D, EventModifiers.Control);
        TestAssert.Require(duplicate.type == EventType.Used &&
                           harness.Scene.gameObjects.Count == beforeDuplicate + 2,
            "Ctrl+D did not duplicate the selected GameObject together with its child hierarchy.");
        TestAssert.Require(harness.SelectedGameObject is { } directCopy && !ReferenceEquals(directCopy, source) &&
                           directCopy.transform.localPosition == source.transform.localPosition,
            "Hierarchy duplicate did not select a copied object with the source transform.");

        harness.Select(source);
        var beforeCopy = harness.Scene.gameObjects.Count;
        var copy = harness.SendHierarchyKey(KeyCode.C, EventModifiers.Control);
        TestAssert.Require(copy.type == EventType.Used && harness.Scene.gameObjects.Count == beforeCopy,
            "Ctrl+C mutated the Hierarchy before Paste.");
        var paste = harness.SendHierarchyKey(KeyCode.V, EventModifiers.Control);
        TestAssert.Require(paste.type == EventType.Used && harness.Scene.gameObjects.Count == beforeCopy + 2,
            "Ctrl+V did not paste the copied GameObject together with its child hierarchy.");
    }

    private static void VerifyDelete()
    {
        using var fixture = new KeyboardCommandFixture();
        using var harness = new EditorKeyboardHarness(fixture);
        var root = harness.Root;
        var id = root.Id;
        harness.Select(root);
        var delete = harness.SendHierarchyKey(KeyCode.Delete);
        TestAssert.Require(delete.type == EventType.Used && harness.Scene.Find(id) is null &&
                           harness.Scene.gameObjects.Count == 0,
            "Delete did not remove the selected GameObject together with its child hierarchy.");
        harness.SendHierarchyKey(KeyCode.Z, EventModifiers.Control);
        TestAssert.Require(harness.Scene.Find(id) is not null,
            "Ctrl+Z did not restore a GameObject removed by the Hierarchy Delete shortcut.");
        harness.SendHierarchyKey(KeyCode.Y, EventModifiers.Control);
        TestAssert.Require(harness.Scene.Find(id) is null,
            "Ctrl+Y did not reapply a Hierarchy Delete after Undo.");
    }
}
