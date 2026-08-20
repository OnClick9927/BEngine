using BEngine.Editor;

namespace BEngine.ExampleTests.EditorKeyboardCommands;

internal static class ProjectKeyboardTests
{
    internal static void Run()
    {
        VerifyRename();
        VerifyDuplicate();
        VerifyCopyPaste();
        VerifyDelete();
    }

    private static void VerifyRename()
    {
        using var fixture = new KeyboardCommandFixture();
        using var harness = new EditorKeyboardHarness(fixture);
        harness.SelectProject("Assets/Shortcuts/Source.txt");
        var rename = harness.SendProjectKey(KeyCode.F2);
        TestAssert.Require(rename.type == EventType.Used &&
                           harness.ProjectRenamingPath == "Assets/Shortcuts/Source.txt",
            "Project F2 did not enter rename mode for the selected asset.");
        harness.SetProjectRenameValue("RenamedSource");
        harness.SendProjectKey(KeyCode.Return);
        TestAssert.Require(File.Exists(Path.Combine(Path.GetDirectoryName(fixture.SourceAssetPath)!,
                "RenamedSource.txt")),
            "Project rename did not preserve the asset extension or move the source file.");
    }

    private static void VerifyDuplicate()
    {
        using var fixture = new KeyboardCommandFixture();
        using var harness = new EditorKeyboardHarness(fixture);
        var directory = Path.GetDirectoryName(fixture.SourceAssetPath)!;
        harness.SelectProject("Assets/Shortcuts/Source.txt");
        var before = EditorKeyboardHarness.CountFiles(directory);
        var duplicate = harness.SendProjectKey(KeyCode.D, EventModifiers.Control);
        TestAssert.Require(duplicate.type == EventType.Used && EditorKeyboardHarness.CountFiles(directory) == before + 1,
            "Ctrl+D did not duplicate the selected Project asset exactly once.");
    }

    private static void VerifyCopyPaste()
    {
        using var fixture = new KeyboardCommandFixture();
        using var harness = new EditorKeyboardHarness(fixture);
        var directory = Path.GetDirectoryName(fixture.SourceAssetPath)!;
        harness.SelectProject("Assets/Shortcuts/Source.txt");
        var before = EditorKeyboardHarness.CountFiles(directory);
        var copy = harness.SendProjectKey(KeyCode.C, EventModifiers.Control);
        TestAssert.Require(copy.type == EventType.Used && EditorKeyboardHarness.CountFiles(directory) == before,
            "Ctrl+C modified the Project before Paste.");
        var paste = harness.SendProjectKey(KeyCode.V, EventModifiers.Control);
        TestAssert.Require(paste.type == EventType.Used && EditorKeyboardHarness.CountFiles(directory) == before + 1,
            "Ctrl+V did not paste the copied Project asset exactly once.");
    }

    private static void VerifyDelete()
    {
        using var fixture = new KeyboardCommandFixture();
        using var harness = new EditorKeyboardHarness(fixture);
        harness.SelectProject("Assets/Shortcuts/Source.txt");
        var delete = harness.SendProjectKey(KeyCode.Delete);
        TestAssert.Require(delete.type == EventType.Used && !File.Exists(fixture.SourceAssetPath),
            "Delete did not remove the selected Project asset.");
    }
}
