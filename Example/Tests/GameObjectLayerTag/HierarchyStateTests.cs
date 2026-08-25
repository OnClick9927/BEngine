namespace BEngine.ExampleTests.GameObjectLayerTag;

internal static class HierarchyStateTests
{
    public static void Run()
    {
        var scene = new Scene("Hierarchy State");
        try
        {
            var activeRoot = scene.CreateGameObject("Active Root");
            var inactiveRoot = scene.CreateGameObject("Inactive Root");
            var child = scene.CreateGameObject("Child");
            var grandchild = scene.CreateGameObject("Grandchild");
            child.transform.SetParent(activeRoot.transform, false);
            grandchild.transform.SetParent(child.transform, false);

            TestAssert.Require(child.activeSelf && child.activeInHierarchy && grandchild.activeInHierarchy,
                "An active hierarchy did not expose active descendants.");
            activeRoot.SetActive(false);
            TestAssert.Require(child.activeSelf && !child.activeInHierarchy && !grandchild.activeInHierarchy,
                "Disabling an ancestor changed activeSelf or failed to propagate activeInHierarchy.");

            inactiveRoot.SetActive(false);
            child.transform.SetParent(inactiveRoot.transform, false);
            TestAssert.Require(child.activeSelf && !child.activeInHierarchy,
                "Reparenting beneath an inactive ancestor did not recompute activeInHierarchy.");
            inactiveRoot.SetActive(true);
            TestAssert.Require(child.activeInHierarchy && grandchild.activeInHierarchy,
                "Re-enabling the root did not restore descendant activeInHierarchy state.");

            child.SetActive(false);
            TestAssert.Require(!child.activeSelf && !child.activeInHierarchy && !grandchild.activeInHierarchy,
                "A locally disabled GameObject did not suppress its descendant hierarchy.");
            child.transform.SetParent(null, false);
            TestAssert.Require(!child.activeInHierarchy && grandchild.activeSelf && !grandchild.activeInHierarchy,
                "Detaching an inactive subtree corrupted activeSelf or activeInHierarchy.");
        }
        finally
        {
            if (scene.isCreated) scene.Dispose();
        }
    }
}
