using BEngine.Editor;

namespace BEngine.ExampleTests.InspectorComponentActions;

internal static class ComponentMenuTests
{
    internal static void Run(InspectorWorkspaceFixture fixture)
    {
        VerifyResetAndUndo(fixture);
        VerifyCopyPasteAndUndo(fixture);
        VerifyEditScriptAndRemove(fixture);
    }

    private static void VerifyResetAndUndo(InspectorWorkspaceFixture fixture)
    {
        Undo.ClearAll();
        var target = new GameObject("Reset Target");
        var component = target.AddComponent<InspectorActionProbe>();
        component.probeValue = 99;
        component.probeLabel = "Changed";
        component.enabled = false;
        using var inspector = new InspectorHarness(target, fixture.Workspace);
        GenericMenuCapture.Install();

        OpenMenu(inspector, "Inspector Action Probe (Script)");
        GenericMenuCapture.Invoke("Reset");
        TestAssert.Require(component.probeValue == 21 && component.probeLabel == "Reset" &&
                           component.enabled && component.resetCalls == 1 && component.validateCalls == 1,
            "Reset did not restore defaults, invoke Reset/OnValidate, and restore enabled state.");
        Undo.PerformUndo();
        TestAssert.Require(component.probeValue == 99 && component.probeLabel == "Changed" && !component.enabled,
            "Undo did not restore component values from before Reset.");
        Undo.PerformRedo();
        TestAssert.Require(component.probeValue == 21 && component.probeLabel == "Reset" && component.enabled,
            "Redo did not reapply Reset component values.");
        Undo.ClearAll();
    }

    private static void VerifyCopyPasteAndUndo(InspectorWorkspaceFixture fixture)
    {
        var sourceOwner = new GameObject("Clipboard Source");
        var source = sourceOwner.AddComponent<InspectorActionProbe>();
        source.probeValue = 73;
        source.probeLabel = "Copied";
        source.enabled = false;
        using (var sourceInspector = new InspectorHarness(sourceOwner, fixture.Workspace))
        {
            GenericMenuCapture.Install();
            OpenMenu(sourceInspector, "Inspector Action Probe (Script)");
            GenericMenuCapture.Invoke("Copy Component");
        }

        Undo.ClearAll();
        var targetOwner = new GameObject("Clipboard Target");
        var target = targetOwner.AddComponent<InspectorActionProbe>();
        target.probeValue = 5;
        target.probeLabel = "Original";
        target.enabled = true;
        using var targetInspector = new InspectorHarness(targetOwner, fixture.Workspace);
        GenericMenuCapture.Install();
        OpenMenu(targetInspector, "Inspector Action Probe (Script)");
        TestAssert.Require(GenericMenuCapture.Items.Any(item =>
                item.Path == "Paste Component Values" && item.Enabled),
            "Paste Component Values was not enabled for an exact component type match.");
        GenericMenuCapture.Invoke("Paste Component Values");
        TestAssert.Require(target.probeValue == 73 && target.probeLabel == "Copied" && !target.enabled &&
                           target.validateCalls == 1,
            "Paste Component Values did not copy serialized fields/enabled or invoke OnValidate.");
        Undo.PerformUndo();
        TestAssert.Require(target.probeValue == 5 && target.probeLabel == "Original" && target.enabled,
            "Undo did not restore values from before Paste Component Values.");
        Undo.PerformRedo();
        TestAssert.Require(target.probeValue == 73 && target.probeLabel == "Copied" && !target.enabled,
            "Redo did not reapply pasted component values.");
        Undo.ClearAll();
    }

    private static void VerifyEditScriptAndRemove(InspectorWorkspaceFixture fixture)
    {
        Undo.ClearAll();
        var target = new GameObject("Remove Target");
        var component = target.AddComponent<InspectorActionProbe>();
        using var inspector = new InspectorHarness(target, fixture.Workspace);
        GenericMenuCapture.Install();
        OpenMenu(inspector, "Inspector Action Probe (Script)");
        TestAssert.Require(GenericMenuCapture.Items.Any(item =>
                item.Path == "Edit Script" && item.Enabled && item.HasAction),
            "Edit Script was not enabled when the component source exists in Assets.");
        TestAssert.Require(Path.GetFullPath(inspector.FindScriptSource(component)!) ==
                           Path.GetFullPath(fixture.ScriptPath),
            "Edit Script did not resolve the selected MonoBehaviour source file.");

        GenericMenuCapture.Invoke("Remove Component");
        TestAssert.Require(target.GetComponent<InspectorActionProbe>() is null,
            "Remove Component did not detach the selected component.");
        Undo.PerformUndo();
        TestAssert.Require(ReferenceEquals(target.GetComponent<InspectorActionProbe>(), component),
            "Undo did not restore the exact removed component instance.");
        Undo.PerformRedo();
        TestAssert.Require(target.GetComponent<InspectorActionProbe>() is null,
            "Redo did not remove the component again.");
        Undo.ClearAll();
    }

    internal static void OpenMenu(InspectorHarness inspector, string componentTitle)
    {
        var commands = inspector.Repaint();
        var title = TestAssert.Text(commands, componentTitle);
        GenericMenuCapture.Reset();
        inspector.ContextClick(title);
        TestAssert.Require(GenericMenuCapture.HasMenu,
            $"Right-clicking '{componentTitle}' did not open its component menu.");
    }
}
