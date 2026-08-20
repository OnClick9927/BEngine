using BEngine.Editor;

namespace BEngine.ExampleTests.InspectorComponentActions;

internal static class RequiredComponentRemovalTests
{
    internal static void Run(InspectorWorkspaceFixture fixture)
    {
        Undo.ClearAll();
        var target = new GameObject("Required Component Removal Target");
        ObjectFactory.AddComponent<RequiredOwnerProbe>(target);
        var leaf = target.GetComponent<RequiredLeafProbe>() ??
                   throw new InvalidOperationException("RequiredLeafProbe was not auto-added.");
        var middle = target.GetComponent<RequiredMiddleProbe>() ??
                     throw new InvalidOperationException("RequiredMiddleProbe was not auto-added.");
        var sibling = target.GetComponent<RequiredSiblingProbe>() ??
                      throw new InvalidOperationException("RequiredSiblingProbe was not auto-added.");
        var requiredComponents = new (Component Component, string Title)[]
        {
            (leaf, "Required Leaf Probe (Script)"),
            (middle, "Required Middle Probe (Script)"),
            (sibling, "Required Sibling Probe (Script)")
        };
        Undo.ClearAll();
        using var inspector = new InspectorHarness(target, fixture.Workspace);
        GenericMenuCapture.Install();

        foreach (var (component, title) in requiredComponents)
        {
            ComponentMenuTests.OpenMenu(inspector, title);
            var remove = GenericMenuCapture.Items.Single(item => item.Path == "Remove Component");
            TestAssert.Require(!remove.Enabled && !remove.HasAction,
                $"The Inspector enabled Remove Component for required component {component.GetType().Name}.");
            var componentCount = target.components.Count;
            TestAssert.Require(!target.RemoveComponent(component),
                $"GameObject.RemoveComponent accepted required component {component.GetType().Name}.");
            TestAssert.Require(target.components.Count == componentCount &&
                               ReferenceEquals(target.GetComponent(component.GetType()), component),
                $"Rejected removal changed required component {component.GetType().Name} or component order.");
        }

        TestAssert.Require(!Undo.canUndo,
            "Rejected required-component removal created an Undo operation.");
        Undo.ClearAll();
    }
}
