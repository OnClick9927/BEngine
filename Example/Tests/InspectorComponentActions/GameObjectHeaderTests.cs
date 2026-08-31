using BEngine.Editor;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.InspectorComponentActions;

internal static class GameObjectHeaderTests
{
    internal static void Run(InspectorWorkspaceFixture fixture)
    {
        Undo.ClearAll();
        var target = new GameObject("Header Before") { tag = "Player", layer = 3 };
        using var inspector = new InspectorHarness(target, fixture.Workspace);
        GenericMenuCapture.Install();

        var commands = inspector.Repaint();
        var oldName = TestAssert.Text(commands, "Header Before");
        inspector.Click(oldName, clickCount: 2);
        GUIUtility.systemCopyBuffer = "Header After";
        inspector.SendKey(KeyCode.V, EventModifiers.Control);
        TestAssert.Require(target.name == "Header After",
            "Editing the GameObject header name did not update the selected object.");

        commands = inspector.Repaint();
        var activeCheck = commands.Where(command => command.Type == GpuCanvasCommandType.Image &&
                                                     command.Content == EditorBuiltinIcons.Toolbar.Check &&
                                                     command.Rect.Y < oldName.Rect.Y + oldName.Rect.Height)
            .OrderBy(command => command.Rect.X)
            .FirstOrDefault();
        TestAssert.Require(activeCheck.Type == GpuCanvasCommandType.Image,
            "The activeSelf checkbox was not visible in the GameObject header.");
        inspector.Click(activeCheck);
        TestAssert.Require(!target.activeSelf,
            "Clicking the GameObject header checkbox did not update activeSelf.");

        commands = inspector.Repaint();
        inspector.Click(TestAssert.Text(commands, "Static"));
        TestAssert.Require(target.isStatic,
            "Clicking the GameObject header Static toggle did not update isStatic.");

        commands = inspector.Repaint();
        GenericMenuCapture.Reset();
        inspector.Click(TestAssert.Text(commands, "Player"));
        TestAssert.Require(GenericMenuCapture.Items.Any(item => item.Path == "Enemy" && item.Enabled),
            "The GameObject Tag field did not expose configured tags.");
        GenericMenuCapture.Invoke("Enemy");
        inspector.Repaint();
        TestAssert.Require(target.tag == "Enemy",
            "Selecting a GameObject Tag menu item did not update tag.");

        commands = inspector.Repaint();
        GenericMenuCapture.Reset();
        inspector.Click(TestAssert.Text(commands, "3  Gameplay"));
        TestAssert.Require(GenericMenuCapture.Items.Any(item => item.Path == "4  Enemies" && item.Enabled),
            "The GameObject Layer field did not expose configured named layers.");
        GenericMenuCapture.Invoke("4  Enemies");
        inspector.Repaint();
        TestAssert.Require(target.layer == SortingLayer.FromIndex(4),
            "Selecting a GameObject Layer menu item did not update layer.");
        TestAssert.Require(Undo.canUndo,
            "GameObject header edits were not recorded in Undo.");
        Undo.ClearAll();
    }
}
