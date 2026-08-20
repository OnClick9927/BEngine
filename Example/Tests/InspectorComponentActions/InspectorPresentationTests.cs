using BEngine.Editor;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.InspectorComponentActions;

internal static class InspectorPresentationTests
{
    internal static void Run(InspectorWorkspaceFixture fixture)
    {
        var target = new GameObject("Presentation Target") { tag = "Player", layer = 8 };
        target.AddComponent<InspectorActionProbe>();
        using var inspector = new InspectorHarness(target, fixture.Workspace);
        GenericMenuCapture.Install();

        var commands = inspector.Repaint();
        TestAssert.Text(commands, "Presentation Target");
        TestAssert.Text(commands, "Static");
        TestAssert.Text(commands, "Tag");
        TestAssert.Text(commands, "Player");
        TestAssert.Text(commands, "Layer");
        TestAssert.Text(commands, "2^3  Gameplay");
        TestAssert.Text(commands, "Transform");
        TestAssert.Text(commands, "Position");
        TestAssert.Text(commands, "Rotation");
        TestAssert.Text(commands, "Scale");
        TestAssert.Require(commands.Count(command => command.Type == GpuCanvasCommandType.Text &&
                                                    command.Content == "R") >= 3,
            "The Transform Inspector did not draw a reset command for Position, Rotation and Scale.");
        TestAssert.Require(!TestAssert.HasText(commands, "Local Rotation"),
            "The Transform Inspector exposed an internal field instead of the 2D Rotation control.");
        var componentTitle = TestAssert.Text(commands, "Inspector Action Probe (Script)");
        TestAssert.Text(commands, "Script");
        TestAssert.Text(commands, "Probe Value");
        TestAssert.Require(commands.Any(command => command.Type == GpuCanvasCommandType.Image &&
                                                   command.Content == EditorBuiltinIcons.Components.GameObject),
            "The GameObject Inspector header did not draw its object icon.");
        TestAssert.Require(commands.Count(command => command.Type == GpuCanvasCommandType.Image &&
                                                    command.Content == EditorBuiltinIcons.Toolbar.More) >= 2,
            "Every component header did not draw a three-dot menu button.");
        TestAssert.Require(commands.Count(command => command.Type == GpuCanvasCommandType.Image &&
                                                    command.Content == EditorBuiltinIcons.Toolbar.FoldoutOpen) >= 2,
            "Every component header did not draw an expanded foldout.");
        TestAssert.Require(commands.Any(command => command.Type == GpuCanvasCommandType.Image &&
                                                   command.Content.EndsWith(
                                                       "Icons/Components/Script.png",
                                                       StringComparison.OrdinalIgnoreCase)),
            "The MonoBehaviour component header did not use the script icon.");

        var foldout = TestAssert.ImageNear(commands, EditorBuiltinIcons.Toolbar.FoldoutOpen,
            componentTitle.Rect.Y);
        var foldoutPoint = InspectorHarness.CommandCenter(foldout);
        inspector.MouseDown(foldoutPoint);
        inspector.MouseUp(foldoutPoint);
        TestAssert.Require(inspector.CollapsedComponentCount == 1,
            $"Clicking the component foldout did not update its collapsed state (hot={inspector.HotControl}).");
        var collapsed = inspector.Repaint();
        TestAssert.Text(collapsed, "Inspector Action Probe (Script)");
        TestAssert.Require(!TestAssert.HasText(collapsed, "Probe Value"),
            "Collapsing a component did not hide its Inspector content.");
        var closedFoldout = TestAssert.ImageNear(collapsed, EditorBuiltinIcons.Toolbar.FoldoutClosed,
            componentTitle.Rect.Y);
        inspector.Click(closedFoldout);

        commands = inspector.Repaint();
        componentTitle = TestAssert.Text(commands, "Inspector Action Probe (Script)");
        GenericMenuCapture.Reset();
        var more = TestAssert.ImageNear(commands, EditorBuiltinIcons.Toolbar.More, componentTitle.Rect.Y);
        inspector.Click(more);
        AssertStandardMenu(GenericMenuCapture.Items);

        GenericMenuCapture.Reset();
        inspector.ContextClick(componentTitle);
        AssertStandardMenu(GenericMenuCapture.Items);
    }

    private static void AssertStandardMenu(IReadOnlyList<MenuItemSnapshot> items)
    {
        foreach (var path in new[]
                 { "Reset", "Copy Component", "Paste Component Values", "Edit Script", "Remove Component" })
            TestAssert.Require(items.Any(item => item.Path == path),
                $"The component context menu omitted '{path}'.");
    }
}
