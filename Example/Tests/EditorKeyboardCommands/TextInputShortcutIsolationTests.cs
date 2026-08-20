using BEngine.Editor;

namespace BEngine.ExampleTests.EditorKeyboardCommands;

internal static class TextInputShortcutIsolationTests
{
    internal static void Run()
    {
        VerifyHierarchyRenameFieldOwnsEditingKeys();
        VerifyProjectRenameFieldOwnsEditingKeys();
    }

    private static void VerifyHierarchyRenameFieldOwnsEditingKeys()
    {
        using var fixture = new KeyboardCommandFixture();
        using var harness = new EditorKeyboardHarness(fixture);
        var root = harness.Root;
        harness.Select(root);
        harness.SendHierarchyKey(KeyCode.F2);
        harness.SetHierarchyRenameValue("HierarchyTextFocus");
        harness.FocusHierarchyRenameField("HierarchyTextFocus");
        var before = harness.Scene.gameObjects.Count;
        Undo.RecordObject(root, "Focused Text Undo Probe");
        root.activeSelf = false;

        harness.SendHierarchyKey(KeyCode.Delete);
        harness.SendHierarchyKey(KeyCode.C, EventModifiers.Control);
        GUIUtility.systemCopyBuffer = "PastedText";
        harness.SendHierarchyKey(KeyCode.V, EventModifiers.Control);
        var editedText = harness.HierarchyRenameValue;
        harness.SendHierarchyKey(KeyCode.D, EventModifiers.Control);
        harness.SendHierarchyKey(KeyCode.Z, EventModifiers.Control);
        harness.SendHierarchyKey(KeyCode.F2);
        TestAssert.Require(harness.Scene.gameObjects.Count == before && harness.Scene.Find(root.Id) is not null &&
                           harness.HierarchyRenamingId == root.Id && !root.activeSelf && Undo.canUndo &&
                           harness.HierarchyRenameValue == editedText,
            "A Hierarchy shortcut escaped the focused rename TextField and mutated the object hierarchy.");
    }

    private static void VerifyProjectRenameFieldOwnsEditingKeys()
    {
        using var fixture = new KeyboardCommandFixture();
        using var harness = new EditorKeyboardHarness(fixture);
        var directory = Path.GetDirectoryName(fixture.TextFocusAssetPath)!;
        harness.SelectProject("Assets/Shortcuts/TextFocus.txt");
        harness.SendProjectKey(KeyCode.F2);
        harness.SetProjectRenameValue("ProjectTextFocus");
        harness.FocusProjectRenameField("ProjectTextFocus");
        var before = EditorKeyboardHarness.CountFiles(directory);
        var undoProbe = new GameObject("Before Focused Project Undo");
        Undo.RecordObject(undoProbe, "Focused Project Text Undo Probe");
        undoProbe.name = "After Focused Project Undo";

        harness.SendProjectKey(KeyCode.Delete);
        harness.SendProjectKey(KeyCode.C, EventModifiers.Control);
        GUIUtility.systemCopyBuffer = "PastedText";
        harness.SendProjectKey(KeyCode.V, EventModifiers.Control);
        var editedText = harness.ProjectRenameValue;
        harness.SendProjectKey(KeyCode.D, EventModifiers.Control);
        harness.SendProjectKey(KeyCode.Z, EventModifiers.Control);
        harness.SendProjectKey(KeyCode.F2);
        TestAssert.Require(File.Exists(fixture.TextFocusAssetPath) &&
                           EditorKeyboardHarness.CountFiles(directory) == before &&
                           harness.ProjectRenamingPath == "Assets/Shortcuts/TextFocus.txt" &&
                           undoProbe.name == "After Focused Project Undo" && Undo.canUndo &&
                           harness.ProjectRenameValue == editedText,
            "A Project shortcut escaped the focused rename TextField and mutated the selected asset.");
    }
}
