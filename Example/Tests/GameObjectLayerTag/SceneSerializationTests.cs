using BEngine.Documents;

namespace BEngine.ExampleTests.GameObjectLayerTag;

internal static class SceneSerializationTests
{
    public static void Run()
    {
        var source = new Scene("Layer Tag Round Trip");
        try
        {
            var root = source.CreateGameObject("Tagged Root");
            root.tag = "Player";
            root.layer = LayerMask.NameToLayer("Gameplay");
            root.isStatic = true;
            var sprite = root.AddComponent<SpriteRenderer>();
            sprite.sortingLayer = SortingLayer.FromIndex(8);
            sprite.orderInLayer = 12;
            var child = source.CreateGameObject("Inactive Child");
            child.tag = "EditorOnly";
            child.layer = LayerMask.NameToLayer("Ignore Raycast");
            child.SetActive(false);
            child.transform.SetParent(root.transform, false);

            var document = Document.FromBObject<SceneDocument>(source);
            var rootDocument = document.GameObjects.Single(item => item.Id == root.Id);
            var childDocument = document.GameObjects.Single(item => item.Id == child.Id);
            TestAssert.Require(rootDocument.Tag == "Player" && rootDocument.Layer == root.layer &&
                               rootDocument.IsStatic,
                "SceneDocument did not capture the root GameObject's Tag, Layer and Static values.");
            TestAssert.Require(childDocument.Parent == root.Id && childDocument.Tag == "EditorOnly" &&
                               childDocument.Layer == child.layer && !childDocument.Active,
                "SceneDocument did not capture child hierarchy, Tag, Layer or activeSelf.");

            var yaml = document.ToYaml();
            var restored = (Scene)Document.FromYaml<SceneDocument>(yaml).ToBObject();
            try
            {
                var restoredRoot = restored.Find("Tagged Root") ??
                                   throw new InvalidOperationException("The root GameObject was lost in YAML.");
                var restoredChild = restored.Find("Inactive Child") ??
                                    throw new InvalidOperationException("The child GameObject was lost in YAML.");
                TestAssert.Require(restoredRoot.tag == "Player" && restoredRoot.layer == root.layer &&
                                   restoredRoot.isStatic,
                    "Scene YAML did not restore the root GameObject's Tag, Layer and Static values.");
                var restoredSprite = restoredRoot.GetComponent<SpriteRenderer>() ??
                                     throw new InvalidOperationException("The SpriteRenderer was lost in YAML.");
                TestAssert.Require(restoredSprite.sortingLayer == sprite.sortingLayer &&
                                   restoredSprite.orderInLayer == sprite.orderInLayer,
                    "Scene YAML did not restore the renderer's ulong sorting layer.");
                TestAssert.Require(ReferenceEquals(restoredChild.transform.parent, restoredRoot.transform) &&
                                   restoredChild.tag == "EditorOnly" && restoredChild.layer == child.layer &&
                                   !restoredChild.activeSelf && !restoredChild.activeInHierarchy,
                    "Scene YAML did not restore child hierarchy, Tag, Layer or active state.");
            }
            finally
            {
                if (restored.world.IsCreated) restored.world.Dispose();
            }

            var projectSettings = new ProjectSettingsDocument
            {
                Tags = TagManager.tags.ToList(),
                SortingLayers = LayerMask.layers.Select(item => new SortingLayerDocument
                {
                    Value = item.Value,
                    Name = item.Name
                }).ToList()
            };
            var restoredSettings = Document.FromYaml<ProjectSettingsDocument>(projectSettings.ToYaml());
            TestAssert.Require(restoredSettings.Tags.SequenceEqual(TagManager.tags) &&
                               restoredSettings.SortingLayers.Count == 63 &&
                               restoredSettings.SortingLayers.Any(item => item.Value == SortingLayer.FromIndex(8) &&
                                                                          item.Name == "Gameplay") &&
                               restoredSettings.SortingLayers.Any(item => item.Value == SortingLayer.Ui &&
                                                                          item.Name == "UI"),
                "Project settings YAML did not preserve configured Tags and all 63 sorting layers.");
        }
        finally
        {
            if (source.world.IsCreated) source.world.Dispose();
        }
    }
}
