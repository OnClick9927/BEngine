namespace BEngine.ExampleTests.GameObjectLayerTag;

internal static class RuntimeLayerTagTests
{
    public static void Run()
    {
        var gameObject = new GameObject("Runtime Identity");
        TestAssert.Require(gameObject.tag == "Untagged",
            "A new GameObject does not use the Unity-compatible Untagged default.");
        TestAssert.Require(gameObject.layer == LayerMask.NameToLayer("Default"),
            "A new GameObject is not assigned to the Default layer.");
        TestAssert.Require(TagManager.tags.SequenceEqual(["Untagged", "Player", "Enemy", "EditorOnly"]),
            "TagManager did not expose the configured project Tag list in stable order.");
        TestAssert.Require(TagManager.IsDefined("Player") && !TagManager.IsDefined("player") &&
                           !TagManager.IsDefined("Missing"),
            "TagManager.IsDefined does not use the configured, case-sensitive Tag identities.");
        TestAssert.Require(LayerMask.layers.Count == 7 &&
                           LayerMask.NameToLayer("Gameplay") == SortingLayer.FromIndex(6) &&
                           LayerMask.NameToLayer("Enemies") == SortingLayer.FromIndex(7),
            "LayerMask did not preserve the configured one-based layer table.");

        gameObject.tag = "Player";
        gameObject.layer = LayerMask.NameToLayer("Gameplay");
        TestAssert.Require(gameObject.CompareTag("Player"),
            "CompareTag does not use the GameObject's exact tag identity.");
        TestAssert.Throws<ArgumentException>(() => gameObject.CompareTag("player"),
            "CompareTag accepted an undefined Tag.");
        TestAssert.Throws<ArgumentException>(() => gameObject.tag = "Missing",
            "GameObject.tag accepted an undefined Tag.");
        TestAssert.Throws<ArgumentOutOfRangeException>(() => gameObject.layer = 0,
            "GameObject.layer accepted zero.");
        TestAssert.Throws<ArgumentOutOfRangeException>(() => gameObject.layer = 64,
            "GameObject.layer accepted an index outside the supported range.");
        TestAssert.Require(gameObject.layer == SortingLayer.FromIndex(6) &&
                           LayerMask.LayerToName(gameObject.layer) == "Gameplay",
            "LayerMask cannot restore the selected GameObject layer name.");
        TestAssert.Require(LayerMask.GetMask("Default", "Gameplay", "Enemies") ==
                           (SortingLayer.ToMask(LayerMask.NameToLayer("Default")) |
                            SortingLayer.ToMask(LayerMask.NameToLayer("Gameplay")) |
                            SortingLayer.ToMask(LayerMask.NameToLayer("Enemies"))),
            "LayerMask.GetMask did not combine named layers.");
        TestAssert.Require(LayerMask.NameToLayer("Missing Layer") == 0,
            "An unknown layer name did not return zero.");
        TestAssert.Require(SortingLayer.IsWorld(SortingLayer.FromIndex(6)) &&
                           SortingLayer.IsUi(SortingLayer.Ui) &&
                           SortingLayer.FromIndex(63) == 63 &&
                           !SortingLayer.IsValid(ulong.MaxValue),
            "Natural layer indices or configured UI metadata are invalid.");
    }
}
