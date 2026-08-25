using BEngine;
using BEngine.Editor;
using BEngine.Editor.Rendering;
using BEngine.Documents;
using BEngine.Serialization;
using BEngine.Editor.Documents;

namespace BEngine.ExampleTests.EditorSettingsWindows;

internal static class Program
{
    private static int Main()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(),
            "BEngine-EditorSettingsWindows-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var previousEditorDataPath = Environment.GetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH");
        Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH",
            Path.Combine(testDirectory, "EditorData"));
        try
        {
            SettingsProviderRegistry.Invalidate();
            var userProviders = SettingsProviderRegistry.GetProviders(SettingsScope.User);
            var projectProviders = SettingsProviderRegistry.GetProviders(SettingsScope.Project);
            Require(userProviders.Any(item => item.settingsPath == "Preferences/General"),
                "Built-in General preferences provider was not discovered.");
            Require(projectProviders.All(item => !item.settingsPath.Equals(
                        "Project/Editor", StringComparison.OrdinalIgnoreCase)),
                "Project Settings must not expose the removed Editor page; editor preferences belong in Preferences.");
            Require(projectProviders.Any(item => item.settingsPath == "Project/Player"),
                "Built-in Player project provider was not discovered.");
            Require(projectProviders.Count(item => item.settingsPath.Equals(
                        "Project/Tags and Layers", StringComparison.OrdinalIgnoreCase)) == 1,
                "Project Settings must expose exactly one unified Tags and Layers provider.");
            Require(userProviders.Any(item => item.settingsPath == "Preferences/Packages/Test Package" &&
                                              item.isPackageProvider),
                "Reflected package preferences provider was not discovered.");
            Require(projectProviders.Any(item => item.settingsPath == "Project/Packages/Test Package" &&
                                                 item.isPackageProvider),
                "Reflected package project provider was not discovered.");
            Require(userProviders.All(item => item.scope == SettingsScope.User) &&
                    projectProviders.All(item => item.scope == SettingsScope.Project),
                "User and project setting scopes were mixed.");

            var preferencesPath = Path.Combine(testDirectory, "Preferences.yaml");
            var preferences = new EditorPreferencesDocument
            {
                Locale = "en-US", EditorScale = 1.25f, EditorFont = "Segoe UI",
                EditorFontSize = 15, EditorTheme = "Light", AutoRefreshAssets = false,
                ShowAssetMetaFiles = true
            };
            preferences.Save(preferencesPath);
            var restored = Document.Load<EditorPreferencesDocument>(preferencesPath);
            Require(restored.Locale == "en-US" && Math.Abs(restored.EditorScale - 1.25f) < .001f &&
                    restored.EditorFont == "Segoe UI" && restored.EditorFontSize == 15 &&
                    restored.EditorTheme == "Light" && !restored.AutoRefreshAssets && restored.ShowAssetMetaFiles,
                "Preferences YAML did not round-trip.");

            EditorAppearance.Apply(restored);
            Require(EditorAppearance.theme == EditorTheme.Light &&
                    EditorAppearance.fontFamily == "Segoe UI" && EditorAppearance.fontSize == 15,
                "Editor appearance did not apply theme and font preferences.");
            Require(Math.Abs((double)GUIUtility.pixelsPerPoint - 1.25) < .001 &&
                    EditorLocalization.locale == "en-US" && !EditorGUIUtility.isProSkin,
                "Editor scale, localization, or theme flag was not applied.");

            var resolverType = typeof(EditorWindow).Assembly.GetType(
                "BEngine.Editor.EditorGpuCanvasResourceResolver", throwOnError: true)!;
            var resolver = (IGpuCanvasTextResolver)resolverType.GetProperty("Shared")!.GetValue(null)!;
            Require(resolver.TryMeasureText("偏好设置", 16, "Microsoft YaHei UI", out var measuredWidth) &&
                    measuredWidth is > 8 and < 180,
                "Unicode editor text did not produce a compact measured width.");
            Require(resolver.TryResolveText("偏好设置", measuredWidth, 30, 16, "Microsoft YaHei UI",
                    out var textTexture) && textTexture.Width == measuredWidth &&
                    textTexture.Format == BEngine.Rendering.Rhi.GraphicsTextureFormat.Rgba8Unorm &&
                    textTexture.Pixels.Span[3..].ToArray().Where((_, index) => index % 4 == 0).Any(alpha => alpha > 0),
                "Unicode editor text did not produce a GPU texture.");

            var projectPath = Path.Combine(testDirectory, "ProjectSettings.yaml");
            EditorProjectSettings.Initialize(projectPath, "Settings Test");
            var project = EditorProjectSettings.current;
            project.CompanyName = "BEngine Tests";
            project.ProductName = "Settings Test";
            project.DefaultScreenWidth = 1600;
            project.DefaultScreenHeight = 900;
            project.GraphicsBackend = "Vulkan";
            EditorProjectSettings.Save();
            var projectRestored = Document.Load<ProjectSettingsDocument>(projectPath);
            Require(projectRestored.CompanyName == "BEngine Tests" && projectRestored.ProductName == "Settings Test" &&
                    projectRestored.DefaultScreenWidth == 1600 && projectRestored.DefaultScreenHeight == 900 &&
                    projectRestored.GraphicsBackend == "Vulkan",
                "Project settings YAML did not round-trip.");

            VerifyTagLayerDraftAndDocumentRewrites();
            var renderMarkers = SettingsWindowRenderRegressionTests.Run(testDirectory, projectPath);
            Console.WriteLine("EDITOR_SETTINGS_WINDOWS_OK|reflection,scopes,yaml,appearance,scale,locale," +
                              $"font-gpu,project,tag-layer-draft,tag-reference-rewrite,{string.Join(',', renderMarkers)}");
            return 0;
        }
        finally
        {
            Environment.SetEnvironmentVariable("BENGINE_EDITOR_DATA_PATH", previousEditorDataPath);
            try { Directory.Delete(testDirectory, true); } catch { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void VerifyTagLayerDraftAndDocumentRewrites()
    {
        var settings = EditorProjectSettings.current;
        var originalTags = settings.Tags.ToList();
        var originalLayers = CloneLayers(settings.SortingLayers);
        try
        {
            settings.Tags = ["Untagged", "Player", "Enemy"];
            settings.SortingLayers = CloneLayers(originalLayers);

            var draft = new TagLayerSettingsDraft();
            draft.Reload();
            Require(!draft.IsDirty && draft.Error.Length == 0,
                "A freshly loaded Tags and Layers draft must be clean and valid.");

            var protectedCount = draft.Tags.Count;
            draft.RenameTag(0, "Renamed Untagged");
            draft.RemoveTag(0);
            Require(draft.Tags.Count == protectedCount && draft.Tags[0] == "Untagged",
                "The mandatory Untagged entry could be renamed or removed.");

            draft.AddTag("Collectible");
            draft.RenameTag(1, "Hero");
            draft.RemoveTag(2);
            draft.SetLayerName(8, "Actors");
            Require(draft.IsDirty && draft.Error.Length == 0 &&
                    draft.Tags.SequenceEqual(["Untagged", "Hero", "Collectible"]) &&
                    draft.TagReplacements.TryGetValue("Player", out var renamed) && renamed == "Hero" &&
                    draft.TagReplacements.TryGetValue("Enemy", out var deleted) && deleted == "Untagged",
                "Tag add, rename, delete, or replacement tracking produced an invalid draft.");

            var emptyTag = NewDraft();
            emptyTag.AddTag("   ");
            Require(emptyTag.Error.Contains("needs a name", StringComparison.OrdinalIgnoreCase),
                "An empty Tag name was accepted.");
            var duplicateTag = NewDraft();
            duplicateTag.AddTag("Player");
            Require(duplicateTag.Error.Contains("duplicated", StringComparison.OrdinalIgnoreCase),
                "A duplicate Tag name was accepted.");

            var emptyLayer = NewDraft();
            emptyLayer.SetLayerName(SortingLayer.MinimumIndex, string.Empty);
            Require(emptyLayer.Error.Contains("needs a name", StringComparison.OrdinalIgnoreCase),
                "An empty Layer name was accepted.");
            var duplicateLayer = NewDraft();
            duplicateLayer.SetLayerName(SortingLayer.MinimumIndex,
                duplicateLayer.SortingLayers[1].Name.ToUpperInvariant());
            Require(duplicateLayer.Error.Contains("duplicated", StringComparison.OrdinalIgnoreCase),
                "A case-insensitive duplicate Layer name was accepted.");

            VerifyDocumentTagRewrites();
            ProjectTagLayerSettingsApplier.Apply(draft);
            var persisted = Document.Load<ProjectSettingsDocument>(EditorProjectSettings.settingsPath);
            Require(persisted.Tags.SequenceEqual(["Untagged", "Hero", "Collectible"]) &&
                    TagManager.tags.SequenceEqual(persisted.Tags),
                "Applying the Tags draft did not update ProjectSettings.yaml and TagManager together.");
            Require(persisted.SortingLayers.Single(layer => layer.Value == SortingLayer.FromIndex(8)).Name ==
                    "Actors" && LayerMask.LayerToName(SortingLayer.FromIndex(8)) == "Actors",
                "Applying the Layers draft did not preserve its value or refresh SortingLayerRegistry.");
        }
        finally
        {
            settings.Tags = originalTags;
            settings.SortingLayers = originalLayers;
            EditorProjectSettings.Save();
        }

        static TagLayerSettingsDraft NewDraft()
        {
            var value = new TagLayerSettingsDraft();
            value.Reload();
            return value;
        }
    }

    private static void VerifyDocumentTagRewrites()
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Player"] = "Hero",
            ["Enemy"] = "Untagged"
        };
        var scene = new SceneDocument
        {
            GameObjects =
            [
                new GameObjectDocument { Name = "Renamed", Tag = "Player" },
                new GameObjectDocument { Name = "Deleted", Tag = "Enemy" },
                new GameObjectDocument { Name = "Untouched", Tag = "EditorOnly" }
            ]
        };
        var sceneChanges = ProjectTagLayerSettingsApplier.RewriteDocumentTags(scene, replacements);
        Require(sceneChanges == 2 &&
                scene.GameObjects.Select(static item => item.Tag)
                    .SequenceEqual(["Hero", "Untagged", "EditorOnly"]),
            "Scene tag references were not rewritten for rename and delete mappings.");

        var prefab = new PrefabDocument
        {
            GameObjects =
            [
                new GameObjectDocument { Name = "Root", Tag = "Player" },
                new GameObjectDocument { Name = "Child", Tag = "Enemy" }
            ]
        };
        var prefabChanges = ProjectTagLayerSettingsApplier.RewriteDocumentTags(prefab, replacements);
        Require(prefabChanges == 2 &&
                prefab.GameObjects.Select(static item => item.Tag).SequenceEqual(["Hero", "Untagged"]),
            "Prefab tag references were not rewritten for rename and delete mappings.");
    }

    private static List<SortingLayerDocument> CloneLayers(IEnumerable<SortingLayerDocument> layers) =>
        layers.Select(static layer => new SortingLayerDocument
        {
            Value = layer.Value,
            Name = layer.Name
        }).ToList();
}

internal static class ReflectedTestPackageSettings
{
    [SettingsProvider]
    private static SettingsProvider UserSettings() => new("Preferences/Packages/Test Package", SettingsScope.User,
        ["extension", "reflection"])
    {
        label = "Test Package",
        guiHandler = _ => GUILayout.Label("Reflected user preferences")
    };

    [SettingsProvider]
    private static SettingsProvider ProjectSettings() => new("Project/Packages/Test Package",
        SettingsScope.Project, ["extension", "reflection"])
    {
        label = "Test Package",
        guiHandler = _ => GUILayout.Label("Reflected project settings")
    };
}
