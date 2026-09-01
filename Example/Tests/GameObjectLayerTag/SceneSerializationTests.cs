using BEngine.ProjectSystem;

namespace BEngine.ExampleTests.GameObjectLayerTag;

internal static class SceneSerializationTests
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BEngineSceneRoundTrip-{Guid.NewGuid():N}");
        var scenePath = Path.Combine(directory, "LayerTag.scene.yaml");
        Scene? restored = null;
        try
        {
            Directory.CreateDirectory(directory);
            var rootId = Guid.NewGuid();
            var childId = Guid.NewGuid();
            var gameplayLayer = LayerMask.NameToLayer("Gameplay");
            var ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            File.WriteAllText(scenePath, CurrentSceneYaml(rootId, childId, gameplayLayer, ignoreRaycastLayer));

            restored = BAsset.Load<Scene>(scenePath) ??
                       throw new InvalidOperationException("The current Scene BAsset could not be loaded.");
            var restoredRoot = restored.Find(rootId) ??
                               throw new InvalidOperationException("The root GameObject was lost in YAML.");
            var restoredChild = restored.Find(childId) ??
                                throw new InvalidOperationException("The child GameObject was lost in YAML.");
            TestAssert.Require(restoredRoot.tag == "Player" && restoredRoot.layer == gameplayLayer &&
                               restoredRoot.isStatic,
                "Scene YAML did not restore the root GameObject's Tag, Layer and Static values.");
            var restoredSprite = restoredRoot.GetComponent<SpriteRenderer>() ??
                                 throw new InvalidOperationException("The SpriteRenderer was lost in YAML.");
            TestAssert.Require(restoredSprite.sortingLayer == gameplayLayer &&
                               restoredSprite.orderInLayer == 12,
                "Scene YAML did not restore the renderer's natural sorting layer and order.");
            TestAssert.Require(ReferenceEquals(restoredChild.transform.parent, restoredRoot.transform) &&
                               restoredChild.tag == "EditorOnly" &&
                               restoredChild.layer == ignoreRaycastLayer &&
                               !restoredChild.activeSelf && !restoredChild.activeInHierarchy,
                "Scene YAML did not restore child hierarchy, Tag, Layer or active state.");

            var projectSettings = new ProjectSettingsData
            {
                Tags = TagManager.tags.ToList(),
                SortingLayers = LayerMask.layers.Select(item => new SortingLayerData
                {
                    Value = item.Value,
                    Name = item.Name,
                    BuiltIn = item.IsBuiltIn,
                    IsUi = item.IsUi,
                    BuiltInId = item.BuiltInId
                }).ToList()
            };
            var settingsYaml = YamlUtility.Serialize(projectSettings);
            var restoredSettings = YamlUtility.Deserialize<ProjectSettingsData>(settingsYaml);
            TestAssert.Require(restoredSettings.Tags.SequenceEqual(TagManager.tags) &&
                               restoredSettings.SortingLayers.Count == LayerMask.layers.Count &&
                               restoredSettings.SortingLayers.Any(item => item.Value == gameplayLayer &&
                                                                          item.Name == "Gameplay") &&
                               restoredSettings.SortingLayers.Any(item => item.Value == SortingLayer.Ui &&
                                                                          item.Name == "UI"),
                "Project settings YAML did not preserve configured Tags and natural sorting layers.");
        }
        finally
        {
            if (restored?.isCreated == true) restored.Dispose();
            BAsset.Invalidate(scenePath);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static string CurrentSceneYaml(Guid rootId, Guid childId, ulong gameplayLayer,
        ulong ignoreRaycastLayer) => $$"""
        format: BEngine.Scene
        version: 2
        id: {{Guid.NewGuid()}}
        name: Layer Tag Round Trip
        gameObjects:
        - id: {{rootId}}
          name: Tagged Root
          active: true
          tag: Player
          layer: {{gameplayLayer}}
          isStatic: true
          transform:
            id: {{Guid.NewGuid()}}
            type: BEngine.Transform
            localPosition: { x: '0', y: '0' }
            localRotation: '0'
            localScale: { x: '1', y: '1' }
            fields: {}
          components:
          - id: {{Guid.NewGuid()}}
            type: BEngine.SpriteRenderer
            enabled: true
            fields:
              sortingLayer: '{{gameplayLayer}}'
              orderInLayer: '12'
        - id: {{childId}}
          name: Inactive Child
          active: false
          tag: EditorOnly
          layer: {{ignoreRaycastLayer}}
          parent: {{rootId}}
          transform:
            id: {{Guid.NewGuid()}}
            type: BEngine.Transform
            localPosition: { x: '0', y: '0' }
            localRotation: '0'
            localScale: { x: '1', y: '1' }
            fields: {}
          components: []
        """;
}
