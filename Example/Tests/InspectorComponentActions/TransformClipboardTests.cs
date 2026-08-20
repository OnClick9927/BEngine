using BEngine.Editor;

namespace BEngine.ExampleTests.InspectorComponentActions;

internal static class TransformClipboardTests
{
    internal static void Run(InspectorWorkspaceFixture fixture)
    {
        var sourceParent = new GameObject("Source Parent");
        sourceParent.transform.localPosition = new Vector2(100, 0);
        var source = new GameObject("Source Child");
        source.transform.SetParent(sourceParent.transform, false);
        source.transform.localPosition = new Vector2(1, 2);
        source.transform.localRotation = 30;
        source.transform.localScale = new Vector2(2, 3);
        using (var sourceInspector = new InspectorHarness(source, fixture.Workspace))
        {
            GenericMenuCapture.Install();
            ComponentMenuTests.OpenMenu(sourceInspector, "Transform");
            TestAssert.Require(!GenericMenuCapture.Items.Any(item => item.Path == "Remove Component"),
                "The required Transform component exposed Remove Component.");
            GenericMenuCapture.Invoke("Copy Component");
        }

        var targetParent = new GameObject("Target Parent");
        targetParent.transform.localPosition = new Vector2(-50, 8);
        var target = new GameObject("Target Child");
        target.transform.SetParent(targetParent.transform, false);
        target.transform.localPosition = new Vector2(9, 8);
        target.transform.localRotation = 1;
        target.transform.localScale = Vector2.one;
        var originalParent = target.transform.parent;
        Undo.ClearAll();
        using var targetInspector = new InspectorHarness(target, fixture.Workspace);
        GenericMenuCapture.Install();
        ComponentMenuTests.OpenMenu(targetInspector, "Transform");
        GenericMenuCapture.Invoke("Paste Component Values");
        AssertLocalState(target.transform, originalParent, new Vector2(1, 2), 30,
            new Vector2(2, 3), "Paste");
        Undo.PerformUndo();
        AssertLocalState(target.transform, originalParent, new Vector2(9, 8), 1,
            Vector2.one, "Undo");
        Undo.PerformRedo();
        AssertLocalState(target.transform, originalParent, new Vector2(1, 2), 30,
            new Vector2(2, 3), "Redo");
        Undo.ClearAll();
    }

    private static void AssertLocalState(Transform transform, Transform? expectedParent, Vector2 position,
        Fix64 rotation, Vector2 scale, string operation)
    {
        TestAssert.Require(ReferenceEquals(transform.parent, expectedParent) &&
                           transform.localPosition == position &&
                           transform.localRotation == rotation &&
                           transform.localScale == scale,
            $"{operation} did not preserve the parent and operate on Transform local values only.");
    }
}
