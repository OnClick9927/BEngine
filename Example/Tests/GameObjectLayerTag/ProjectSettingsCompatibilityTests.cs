using BEngine.ProjectSystem;

namespace BEngine.ExampleTests.GameObjectLayerTag;

internal static class ProjectSettingsCompatibilityTests
{
    private static readonly string[] BuiltInTags =
        ["Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController"];

    public static void Run()
    {
        VerifyDefaults(YamlUtility.Deserialize<ProjectSettingsData>("""
            format: BEngine.ProjectSettings
            version: 3
            productName: Current Project
            """));
        VerifyDefaults(YamlUtility.Deserialize<ProjectSettingsData>("""
            format: BEngine.ProjectSettings
            version: 3
            tags: null
            """));
        VerifyDefaults(YamlUtility.Deserialize<ProjectSettingsData>("""
            format: BEngine.ProjectSettings
            version: 3
            tags: []
            """));
        VerifyNaturalLayerNamesArePreserved();
        VerifyObsoleteAssetVersionsAreRejected();
        VerifyRuntimeSettingsContract();
    }

    private static void VerifyDefaults(ProjectSettingsData settings)
    {
        TestAssert.Require(settings.Tags.SequenceEqual(BuiltInTags),
            "Current project settings did not retain the built-in Tag defaults.");
        TestAssert.Require(settings.SortingLayers.Count == SortingLayer.BuiltInLayerCount &&
                           settings.SortingLayers.Select(static layer => layer.Value)
                               .SequenceEqual(Enumerable.Range(SortingLayer.MinimumIndex,
                                       SortingLayer.BuiltInLayerCount)
                                   .Select(static index => (ulong)index)),
            "Current project settings did not retain the five natural-index built-in Layers.");
    }

    private static void VerifyNaturalLayerNamesArePreserved()
    {
        var source = CurrentSettings();
        var restored = YamlUtility.Deserialize<ProjectSettingsData>(YamlUtility.Serialize(source));
        TestAssert.Require(restored.Version == 3 &&
                           restored.SortingLayers.Select(static layer => layer.Name)
                               .SequenceEqual(["Base", "Characters", "Effects", "Overlay", "HUD"]) &&
                           restored.SortingLayers.Select(static layer => layer.Value)
                               .SequenceEqual([1UL, 2UL, 3UL, 4UL, 5UL]) &&
                           restored.SortingLayers.Count(static layer => layer.BuiltIn) == 5 &&
                           restored.SortingLayers[4].IsUi,
            "Current natural-index Layers lost their names or built-in metadata during YAML round-trip.");
    }

    private static void VerifyRuntimeSettingsContract()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BEngineProjectSettings-{Guid.NewGuid():N}");
        var previousTags = TagManager.tags.ToArray();
        var previousLayers = SortingLayerRegistry.layers.ToArray();
        try
        {
            Directory.CreateDirectory(root);
            YamlUtility.Save(new ProjectData { Name = "Current Settings" },
                Path.Combine(root, ProjectWorkspace.ProjectFileName));
            var workspace = ProjectWorkspace.Open(root);

            var unsupported = CurrentSettings();
            unsupported.Version = 2;
            YamlUtility.Save(unsupported, workspace.ProjectSettingsFilePath);
            TestAssert.Throws<InvalidDataException>(() => ProjectRuntimeSettings.LoadAndApply(workspace),
                "ProjectRuntimeSettings accepted an obsolete project-settings version.");

            var current = CurrentSettings();
            current.Tags = [.. BuiltInTags, "Enemy"];
            YamlUtility.Save(current, workspace.ProjectSettingsFilePath);
            var loaded = ProjectRuntimeSettings.LoadAndApply(workspace);
            var persisted = YamlUtility.Load<ProjectSettingsData>(workspace.ProjectSettingsFilePath);
            TestAssert.Require(loaded.Version == 3 && persisted.Version == 3 &&
                               loaded.Tags.SequenceEqual(current.Tags) &&
                               persisted.SortingLayers.Select(static layer => layer.Name)
                                   .SequenceEqual(current.SortingLayers.Select(static layer => layer.Name)) &&
                               SortingLayerRegistry.layers.Select(static layer => layer.Value)
                                   .SequenceEqual([1UL, 2UL, 3UL, 4UL, 5UL]),
                "Current project settings were not loaded and applied without a legacy migration step.");
        }
        finally
        {
            TagManager.Configure(previousTags);
            SortingLayerRegistry.Configure(previousLayers);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void VerifyObsoleteAssetVersionsAreRejected()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BEngineAssetVersion-{Guid.NewGuid():N}");
        var scenePath = Path.Combine(directory, "Obsolete.scene.yaml");
        var prefabPath = Path.Combine(directory, "Obsolete.prefab.yaml");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(scenePath, """
                format: BEngine.Scene
                version: 1
                name: Obsolete Scene
                gameObjects: []
                """);
            File.WriteAllText(prefabPath, """
                format: BEngine.Prefab
                version: 1
                name: Obsolete Prefab
                gameObjects: []
                """);
            TestAssert.Throws<InvalidDataException>(() => BAsset.Load<Scene>(scenePath),
                "BAsset.Load accepted an obsolete Scene document version.");
            TestAssert.Throws<InvalidDataException>(() => BAsset.Load<PrefabAsset>(prefabPath),
                "BAsset.Load accepted an obsolete Prefab document version.");
        }
        finally
        {
            BAsset.Invalidate(scenePath);
            BAsset.Invalidate(prefabPath);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static ProjectSettingsData CurrentSettings()
    {
        var settings = new ProjectSettingsData();
        var names = new[] { "Base", "Characters", "Effects", "Overlay", "HUD" };
        for (var index = 0; index < names.Length; index++) settings.SortingLayers[index].Name = names[index];
        return settings;
    }
}
