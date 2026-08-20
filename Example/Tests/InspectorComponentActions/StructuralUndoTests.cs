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
        Undo.ClearAll();
    }
}
