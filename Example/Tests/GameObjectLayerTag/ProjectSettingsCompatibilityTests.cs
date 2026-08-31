using BEngine.Documents;
using BEngine.ProjectSystem;

namespace BEngine.ExampleTests.GameObjectLayerTag;

internal static class ProjectSettingsCompatibilityTests
{
    private static readonly string[] BuiltInTags =
        ["Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController"];

    public static void Run()
    {
        VerifyDefaults(Document.FromYaml<ProjectSettingsDocument>("""
            format: BEngine.ProjectSettings
            version: 1
            productName: Legacy Project
            """));
        VerifyDefaults(Document.FromYaml<ProjectSettingsDocument>("""
            format: BEngine.ProjectSettings
            version: 1
            tags: null
            """));
        VerifyDefaults(Document.FromYaml<ProjectSettingsDocument>("""
            format: BEngine.ProjectSettings
            version: 1
            tags: []
            """));
        VerifyPartialSortingLayerMigration();
        VerifyNaturalLayerNamesArePreserved();
        VerifySceneAndPrefabLayerMigration();
        VerifyRuntimeMigrationIsPersisted();

        var previous = TagManager.tags.ToArray();
        try
        {
            TagManager.Configure(BuiltInTags);
            var scene = new SceneDocument
            {
                Name = "Legacy Main Camera",
                GameObjects =
                [
                    new GameObjectDocument
                    {
                        Id = Guid.NewGuid(),
                        Name = "Example Camera",
                        Tag = "MainCamera"
                    }
                ]
            };
            _ = Document.FromYaml<SceneDocument>(scene.ToYaml());
        }
        finally
        {
            TagManager.Configure(previous);
        }
    }

    private static void VerifyDefaults(ProjectSettingsDocument settings)
    {
        TestAssert.Require(settings.Tags.SequenceEqual(BuiltInTags),
            "Legacy project settings did not migrate to the built-in Tag list.");
    }

    private static void VerifyPartialSortingLayerMigration()
    {
        var settings = Document.FromYaml<ProjectSettingsDocument>(LegacySevenLayerSettings());
        TestAssert.Require(settings.Version == 3 &&
                           settings.SortingLayers.Count == SortingLayer.MaximumIndex,
            "Legacy partial sorting layers were not expanded to all 63 layers.");
        TestAssert.Require(settings.SortingLayers.Single(layer => layer.Value == SortingLayer.Default).Name ==
                           "Default" &&
                           settings.SortingLayers.Single(layer => layer.Value == SortingLayer.FromIndex(2)).Name ==
                           "Foreground" &&
                           settings.SortingLayers.Single(layer => layer.Value == SortingLayer.Ui).Name == "UI" &&
                           settings.SortingLayers.Single(layer => layer.Value == SortingLayer.Ui).IsUi,
            "Legacy sorting layer names were not preserved during migration.");
        TestAssert.Require(settings.SortingLayers.Select(layer => layer.Name)
                               .Distinct(StringComparer.OrdinalIgnoreCase).Count() == SortingLayer.MaximumIndex,
            "Migrated sorting layer names are not unique.");
    }

    private static void VerifyNaturalLayerNamesArePreserved()
    {
        var settings = Document.FromYaml<ProjectSettingsDocument>("""
            format: BEngine.ProjectSettings
            version: 2
            sortingLayers:
            - value: 1
              name: Base
            - value: 2
              name: Characters
            - value: 3
              name: Effects
            - value: 4
              name: Overlay
            - value: 5
              name: HUD
            """);
        TestAssert.Require(settings.Version == 3 &&
                           settings.SortingLayers.Select(static layer => layer.Name)
                               .SequenceEqual(["Base", "Characters", "Effects", "Overlay", "HUD"]) &&
                           settings.SortingLayers.Count(static layer => layer.BuiltIn) == 5 &&
                           settings.SortingLayers[4].IsUi,
            "A pre-v3 natural-index layer table lost its custom names or built-in metadata.");
    }

    private static void VerifySceneAndPrefabLayerMigration()
    {
        const string gameObjectId = "11111111-1111-1111-1111-111111111111";
        var scene = Document.FromYaml<SceneDocument>($"""
            format: BEngine.Scene
            version: 1
            name: Legacy Layers
            gameObjects:
            - id: {gameObjectId}
              name: Legacy Object
              active: true
              tag: Untagged
              layer: 32
              components:
              - type: BEngine.Camera2D
                enabled: true
                fields:
                  sortingLayer: 576460752303423488
                  cullingMask: 576460752303423520
              - type: Example.ExternalBusinessComponent
                enabled: true
                fields:
                  layer: 32
            """);
        var migrated = scene.GameObjects.Single();
        var fields = migrated.Components[0].Fields;
        var externalFields = migrated.Components[1].Fields;
        var expectedMask = SortingLayer.ToMask(6) | SortingLayer.ToMask(SortingLayer.Ui);
        TestAssert.Require(scene.Version == 2 && migrated.Layer == 6 &&
                           fields["sortingLayer"] == SortingLayer.Ui.ToString() &&
                           fields["cullingMask"] == expectedMask.ToString() &&
                           externalFields["layer"] == "32",
            "Legacy Scene layer values and masks did not use the same old-to-new index mapping.");

        var prefab = Document.FromYaml<PrefabDocument>($"""
            format: BEngine.Prefab
            version: 1
            root: {gameObjectId}
            gameObjects:
            - id: {gameObjectId}
              name: Legacy Root
              active: true
              tag: Untagged
              layer: 2
              components: []
            """);
        TestAssert.Require(prefab.Version == 2 && prefab.GameObjects.Single().Layer == SortingLayer.Default,
            "Legacy Prefab layer values were not migrated to natural indices.");
    }

    private static void VerifyRuntimeMigrationIsPersisted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineSortingLayerMigration-{Guid.NewGuid():N}");
        var previousTags = TagManager.tags.ToArray();
        var previousLayers = SortingLayerRegistry.layers.ToArray();
        try
        {
            Directory.CreateDirectory(root);
            new ProjectDocument { Name = "Legacy" }
                .Save(Path.Combine(root, ProjectWorkspace.ProjectFileName));
            var workspace = ProjectWorkspace.Open(root);
            File.WriteAllText(workspace.ProjectSettingsFilePath, LegacySevenLayerSettings());
            var settings = ProjectRuntimeSettings.LoadAndApply(workspace);
            var persisted = Document.Load<ProjectSettingsDocument>(workspace.ProjectSettingsFilePath);
            TestAssert.Require(settings.SortingLayers.Count == SortingLayer.MaximumIndex &&
                               persisted.SortingLayers.Count == SortingLayer.MaximumIndex &&
                           persisted.Version == 3,
                "Runtime project settings migration was not persisted.");
        }
        finally
        {
            TagManager.Configure(previousTags);
            SortingLayerRegistry.Configure(previousLayers);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string LegacySevenLayerSettings() => """
        format: BEngine.ProjectSettings
        version: 2
        companyName: DefaultCompany
        productName: Legacy Project
        defaultScreenWidth: 1280
        defaultScreenHeight: 720
        sortingLayers:
        - value: 2
          name: Default
        - value: 4
          name: Foreground
        - value: 576460752303423488
          name: UI
        - value: 1152921504606846976
          name: UI Overlay 1
        - value: 2305843009213693952
          name: UI Overlay 2
        - value: 4611686018427387904
          name: UI Overlay 3
        - value: 9223372036854775808
          name: UI Overlay 4
        tags:
        - Untagged
        - MainCamera
        """;
}
