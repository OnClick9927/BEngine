using BEngine.Editor;

namespace BEngine.ExampleTests.InspectorComponentActions;

internal static class RequireComponentUndoTests
{
    internal static void Run()
    {
        Undo.ClearAll();
        var target = new GameObject("Require Component Undo Target");
        var owner = ObjectFactory.AddComponent<RequiredOwnerProbe>(target);
        var leaf = target.GetComponent<RequiredLeafProbe>();
        var middle = target.GetComponent<RequiredMiddleProbe>();
        var sibling = target.GetComponent<RequiredSiblingProbe>();
        TestAssert.Require(leaf is not null && middle is not null && sibling is not null &&
                           ReferenceEquals(target.GetComponent<RequiredOwnerProbe>(), owner),
            "ObjectFactory did not add every direct and nested RequireComponent dependency.");
        var createdOrder = target.components.Skip(1).ToArray();
        TestAssert.Require(createdOrder.Length == 4 &&
                           ReferenceEquals(createdOrder[0], leaf) &&
                           ReferenceEquals(createdOrder[1], middle) &&
                           ReferenceEquals(createdOrder[2], sibling) &&
                           ReferenceEquals(createdOrder[3], owner),
            "RequireComponent dependencies were not attached before their dependent component.");

        Undo.PerformUndo();
        TestAssert.Require(target.components.Count == 1 &&
                           target.GetComponent<RequiredLeafProbe>() is null &&
                           target.GetComponent<RequiredMiddleProbe>() is null &&
                           target.GetComponent<RequiredSiblingProbe>() is null &&
                           target.GetComponent<RequiredOwnerProbe>() is null,
            "One Undo did not remove the requested component and every auto-added dependency.");
        TestAssert.Require(!Undo.canUndo && Undo.canRedo,
            "RequireComponent additions were registered as more than one Undo operation.");

        Undo.PerformRedo();
        var restoredOrder = target.components.Skip(1).ToArray();
        TestAssert.Require(restoredOrder.Length == createdOrder.Length &&
                           restoredOrder.Zip(createdOrder).All(pair =>
                               ReferenceEquals(pair.First, pair.Second)) &&
                           ReferenceEquals(target.GetComponent<RequiredLeafProbe>(), leaf) &&
                           ReferenceEquals(target.GetComponent<RequiredMiddleProbe>(), middle) &&
                           ReferenceEquals(target.GetComponent<RequiredSiblingProbe>(), sibling) &&
                           ReferenceEquals(target.GetComponent<RequiredOwnerProbe>(), owner),
            "One Redo did not restore every RequireComponent instance in its original order.");
        TestAssert.Require(Undo.canUndo && !Undo.canRedo,
            "RequireComponent Redo left additional redo operations behind.");
        Undo.ClearAll();
    }
}
