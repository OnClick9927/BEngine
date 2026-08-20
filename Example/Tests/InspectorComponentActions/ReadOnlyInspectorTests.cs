using BEngine.Editor;
using BEngine.Editor.Rendering;

namespace BEngine.ExampleTests.InspectorComponentActions;

internal static class ReadOnlyInspectorTests
{
    private static readonly string[] MutationMenuItems =
        ["Reset", "Paste Component Values", "Remove Component"];

    internal static void Run(InspectorWorkspaceFixture fixture)
    {
        CopyProbeForPaste(fixture);
        Undo.ClearAll();
        var target = new GameObject("Read Only Target")
        {
            activeSelf = true,
            isStatic = false,
            tag = "Player",
            layer = 8,
            hideFlags = HideFlags.NotEditable
        };
        var component = target.AddComponent<InspectorActionProbe>();
        component.probeValue = 88;
        component.probeLabel = "Immutable";
        using var inspector = new InspectorHarness(target, fixture.Workspace);
        GenericMenuCapture.Install();

        var commands = inspector.Repaint();
        var oldName = TestAssert.Text(commands, "Read Only Target");
        inspector.Click(oldName, clickCount: 2);
        GUIUtility.systemCopyBuffer = "Mutated";
        inspector.SendKey(KeyCode.V, EventModifiers.Control);

        commands = inspector.Repaint();
        var activeCheck = commands.Where(command => command.Type == GpuCanvasCommandType.Image &&
                                                     command.Content == EditorBuiltinIcons.Toolbar.Check &&
                                                     command.Rect.Y < oldName.Rect.Y + oldName.Rect.Height)
            .OrderBy(command => command.Rect.X)
            .First();
        inspector.Click(activeCheck);
        inspector.Click(TestAssert.Text(inspector.Repaint(), "Static"));

        GenericMenuCapture.Reset();
        inspector.Click(TestAssert.Text(inspector.Repaint(), "Player"));
        TestAssert.Require(!GenericMenuCapture.HasMenu,
            "A read-only GameObject opened its Tag mutation menu.");
        GenericMenuCapture.Reset();
        inspector.Click(TestAssert.Text(inspector.Repaint(), "2^3  Gameplay"));
        TestAssert.Require(!GenericMenuCapture.HasMenu,
            "A read-only GameObject opened its Layer mutation menu.");

        commands = inspector.Repaint();
        var componentTitle = TestAssert.Text(commands, "Inspector Action Probe (Script)");
        inspector.Click(componentTitle);
        var foldout = TestAssert.ImageNear(commands, EditorBuiltinIcons.Toolbar.FoldoutOpen,
            componentTitle.Rect.Y);
        inspector.Click(foldout);
        TestAssert.Require(!TestAssert.HasText(inspector.Repaint(), "Probe Value"),
            "A read-only component could not be collapsed for inspection.");

        GenericMenuCapture.Reset();
        inspector.ContextClick(componentTitle);
        if (GenericMenuCapture.HasMenu)
        {
            var enabledMutations = GenericMenuCapture.Items.Where(item => item.Enabled &&
                MutationMenuItems.Contains(item.Path, StringComparer.Ordinal)).ToArray();
            TestAssert.Require(enabledMutations.Length == 0,
                "A read-only component context menu exposed enabled mutation commands: " +
                string.Join(", ", enabledMutations.Select(item => item.Path)));
        }

        GenericMenuCapture.Reset();
        inspector.Click(TestAssert.Text(inspector.Repaint(), "Add Component"));
        TestAssert.Require(!GenericMenuCapture.HasMenu,
            "A read-only GameObject opened the Add Component menu.");
        TestAssert.Require(target.name == "Read Only Target" && target.activeSelf && !target.isStatic &&
                           target.tag == "Player" && target.layer == 8 && component.enabled &&
                           component.probeValue == 88 && component.probeLabel == "Immutable" &&
                           target.components.Count == 2 && !Undo.canUndo,
            "Inspector interaction mutated a HideFlags.NotEditable GameObject or recorded Undo.");
        Undo.ClearAll();
    }

    private static void CopyProbeForPaste(InspectorWorkspaceFixture fixture)
    {
        var source = new GameObject("Read Only Clipboard Source");
        source.AddComponent<InspectorActionProbe>();
        using var inspector = new InspectorHarness(source, fixture.Workspace);
        GenericMenuCapture.Install();
        ComponentMenuTests.OpenMenu(inspector, "Inspector Action Probe (Script)");
        GenericMenuCapture.Invoke("Copy Component");
    }
}
