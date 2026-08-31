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
            Require(userProviders.Any(item => item.settingsPath == "Preferences/Theme"),
                "Built-in Theme preferences provider was not discovered as a separate page.");
            Require(projectProviders.All(item => !item.settingsPath.Equals(
                        "Project/Editor", StringComparison.OrdinalIgnoreCase)),
                "Project Settings must not expose the removed Editor page; editor preferences belong in Preferences.");
            Require(projectProviders.Any(item => item.settingsPath == "Project/Player"),
                "Built-in Player project provider was not discovered.");
            Require(projectProviders.Count(item => item.settingsPath.Equals(
                        "Project/Tags and Layers", StringComparison.OrdinalIgnoreCase)) == 1 &&
                    projectProviders.All(item => !item.settingsPath.Equals(
                        "Project/Tags", StringComparison.OrdinalIgnoreCase) &&
                        !item.settingsPath.Equals("Project/Layers", StringComparison.OrdinalIgnoreCase)),
                "Project Settings must expose one unified Tags and Layers provider.");
            Require(TagLayerSettingsProvider.ResolveSettingsPath("Project/Tags") ==
                        "Project/Tags and Layers" &&
                    TagLayerSettingsProvider.ResolveSettingsPath("Project/Sorting Layers") ==
                        "Project/Tags and Layers",
                "Legacy Tags or Sorting Layers paths did not resolve to the unified provider tabs.");
            Require(userProviders.Any(item => item.settingsPath == "Preferences/Packages/Test Package" &&
                                              item.isPackageProvider),
                "Reflected package preferences provider was not discovered.");
            Require(projectProviders.Any(item => item.settingsPath == "Project/Packages/Test Package" &&
                                                 item.isPackageProvider),
                "Reflected package project provider was not discovered.");
            Require(userProviders.All(item => item.scope == SettingsScope.User) &&
                    projectProviders.All(item => item.scope == SettingsScope.Project),
                "User and project setting scopes were mixed.");

            VerifyDockableSettingsWindows();
            VerifyEditorScaleRange(testDirectory);

            var preferencesPath = Path.Combine(testDirectory, "Preferences.yaml");
            var preferences = new EditorPreferencesDocument
            {
                Locale = "en-US", EditorScale = 1.25f, EditorFont = "Segoe UI",
                EditorFontSize = 15, EditorTheme = "Light", EditorSkin = "builtin:Light",
                AutoRefreshAssets = false,
                ShowAssetMetaFiles = true
            };
            preferences.Save(preferencesPath);
            var restored = Document.Load<EditorPreferencesDocument>(preferencesPath);
            Require(restored.Locale == "en-US" && Math.Abs(restored.EditorScale - 1.25f) < .001f &&
                    restored.EditorFont == "Segoe UI" && restored.EditorFontSize == 15 &&
                    restored.EditorTheme == "Light" && restored.EditorSkin == "builtin:Light" &&
                    !restored.AutoRefreshAssets && restored.ShowAssetMetaFiles,
                "Preferences YAML did not round-trip.");

            EditorAppearance.Apply(restored);
            Require(EditorAppearance.theme == EditorTheme.Light &&
                    EditorAppearance.activeSkin.name == nameof(EditorTheme.Light) &&
                    EditorAppearance.fontFamily == "Segoe UI" &&
                    EditorAppearance.fontSize == EditorAppearance.DefaultFontSize &&
                    restored.EditorFontSize == EditorAppearance.DefaultFontSize,
                "Editor appearance did not apply the theme or normalize the fixed 14px font.");
            Require(Math.Abs((double)GUIUtility.pixelsPerPoint - 1.25) < .001 &&
                    EditorLocalization.locale == "en-US" && !EditorGUIUtility.isProSkin,
                "Editor scale, localization, or theme flag was not applied.");

            VerifyCustomSkinAndLegacyFontMigration(testDirectory);
            EditorAppearance.Apply(restored);

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
            Console.WriteLine("EDITOR_SETTINGS_WINDOWS_OK|reflection,scopes,yaml,appearance,scale," +
                              "scale-range,settings-dock-reuse,locale," +
                              "fixed-font,custom-theme-presets,custom-theme-persistence,skin-selection," +
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

    private static void VerifyDockableSettingsWindows()
    {
        PreferencesWindow? preferences = null;
        ProjectSettingsWindow? projectSettings = null;
        try
        {
            preferences = PreferencesWindow.Open("Preferences/General");
            Require(preferences.ConsumeRequestedState() == EditorWindowState.Normal &&
                    preferences.saveToLayout,
                "Preferences did not open as a dockable, layout-persistent EditorWindow.");
            preferences.OpenInternal();
            var reopenedPreferences = PreferencesWindow.Open("Preferences/Theme");
            Require(ReferenceEquals(preferences, reopenedPreferences) &&
                    reopenedPreferences.ConsumeRequestedState() == EditorWindowState.Normal,
                "Opening Preferences again did not reuse and focus its dockable window.");

            projectSettings = ProjectSettingsWindow.Open("Project/Player");
            Require(projectSettings.ConsumeRequestedState() == EditorWindowState.Normal &&
                    projectSettings.saveToLayout,
                "Project Settings did not open as a dockable, layout-persistent EditorWindow.");
            projectSettings.OpenInternal();
            var reopenedProjectSettings = ProjectSettingsWindow.Open("Project/Graphics");
            Require(ReferenceEquals(projectSettings, reopenedProjectSettings) &&
                    reopenedProjectSettings.ConsumeRequestedState() == EditorWindowState.Normal,
                "Opening Project Settings again did not reuse and focus its dockable window.");
        }
        finally
        {
            projectSettings?.CloseInternal();
            preferences?.CloseInternal();
        }
    }

    private static void VerifyEditorScaleRange(string testDirectory)
    {
        Require(Math.Abs(EditorAppearance.MinimumScale - 0.5f) < .001f &&
                Math.Abs(EditorAppearance.MaximumScale - 1.8f) < .001f,
            "The public editor scale contract is not 0.5 through 1.8.");

        foreach (var endpoint in new[] { EditorAppearance.MinimumScale, EditorAppearance.MaximumScale })
        {
            var path = Path.Combine(testDirectory, $"Scale-{endpoint:0.0}.yaml");
            new EditorPreferencesDocument { EditorScale = endpoint }.Save(path);
            var restored = Document.Load<EditorPreferencesDocument>(path);
            EditorAppearance.Apply(restored);
            Require(Math.Abs(restored.EditorScale - endpoint) < .001f &&
                    Math.Abs((double)GUIUtility.pixelsPerPoint - endpoint) < .001,
                $"Editor scale endpoint {endpoint:0.0} was not accepted and applied exactly.");
        }

        foreach (var invalid in new[] { 0.49f, 1.81f, float.NaN })
        {
            try
            {
                new EditorPreferencesDocument { EditorScale = invalid }.Save(
                    Path.Combine(testDirectory, $"InvalidScale-{Guid.NewGuid():N}.yaml"));
                throw new InvalidOperationException($"Invalid editor scale {invalid} was persisted.");
            }
            catch (InvalidDataException)
            {
            }
        }

        var below = new EditorPreferencesDocument { EditorScale = -1 };
        EditorAppearance.Apply(below);
        var above = new EditorPreferencesDocument { EditorScale = 5 };
        EditorAppearance.Apply(above);
        var nonFinite = new EditorPreferencesDocument { EditorScale = float.NaN };
        EditorAppearance.Apply(nonFinite);
        Require(Math.Abs(below.EditorScale - EditorAppearance.MinimumScale) < .001f &&
                Math.Abs(above.EditorScale - EditorAppearance.MaximumScale) < .001f &&
                Math.Abs(nonFinite.EditorScale - 1f) < .001f,
            "Direct appearance application did not normalize out-of-range scale values.");
    }

    private static void VerifyCustomSkinAndLegacyFontMigration(string testDirectory)
    {
        var themePath = Path.Combine(testDirectory, "CustomThemePreferences.yaml");
        var customSkin = EditorSkinPreferences.CreateSkin(EditorTheme.Light);
        foreach (var preset in new[] { EditorTheme.Light, EditorTheme.Dark, EditorTheme.Classic })
        {
            Require(EditorSkinPreferences.ApplyPreset(customSkin, preset),
                $"{preset} could not be copied into the custom GUISkin.");
            var builtIn = EditorAppearance.builtInSkins.Single(skin => skin.name == preset.ToString());
            Require(customSkin.button.normal.backgroundColor.Equals(builtIn.button.normal.backgroundColor) &&
                    customSkin.window.normal.backgroundColor.Equals(builtIn.window.normal.backgroundColor) &&
                    customSkin.textField.focused.borderColor.Equals(builtIn.textField.focused.borderColor),
                $"{preset} did not copy the complete GUIStyle preset.");
        }

        var accent = new Color((Fix64).11f, (Fix64).42f, (Fix64).73f, Fix64.One);
        customSkin.selectionRect.normal.backgroundColor = accent;
        Require(EditorSkinPreferences.SaveSkin(customSkin), "The customized GUISkin could not be saved.");
        var custom = new EditorPreferencesDocument
        {
            EditorTheme = nameof(EditorTheme.Custom),
            EditorSkin = EditorSkinPreferences.GetToken(customSkin)
        };
        custom.Save(themePath);
        var customRestored = Document.Load<EditorPreferencesDocument>(themePath);
        EditorAppearance.Apply(customRestored);
        Require(EditorAppearance.theme == EditorTheme.Custom &&
                ColorNear(EditorAppearance.activeSkin.selectionRect.normal.backgroundColor, accent),
            "A customized GUIStyle state was not persisted and applied.");

        var legacyPath = EditorDataPaths.preferencesPath;
        new EditorPreferencesDocument { Locale = "en-US", EditorFontSize = 24 }.Save(legacyPath);
        var appearanceChangeCount = 0;
        void OnAppearanceChanged() => appearanceChangeCount++;
        EditorAppearance.appearanceChanged += OnAppearanceChanged;
        try
        {
            EditorPreferences.Initialize();
            EditorPreferences.Initialize();
        }
        finally
        {
            EditorAppearance.appearanceChanged -= OnAppearanceChanged;
        }
        Require(EditorPreferences.current.EditorFontSize == EditorAppearance.DefaultFontSize &&
                EditorAppearance.fontSize == EditorAppearance.DefaultFontSize,
            "A legacy variable font size was not normalized to 14 when preferences loaded.");
        Require(appearanceChangeCount == 1,
            "Repeated editor preference initialization reapplied the skin or emitted duplicate appearance changes.");
        EditorPreferences.Save();
        Require(Document.Load<EditorPreferencesDocument>(legacyPath).EditorFontSize ==
                EditorAppearance.DefaultFontSize,
            "The normalized fixed font size was not persisted back to preferences.");
    }

    private static bool ColorNear(Color left, Color right)
    {
        const double tolerance = 1d / 255d + .0001d;
        return Math.Abs((double)(left.r - right.r)) <= tolerance &&
               Math.Abs((double)(left.g - right.g)) <= tolerance &&
               Math.Abs((double)(left.b - right.b)) <= tolerance &&
               Math.Abs((double)(left.a - right.a)) <= tolerance;
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
            var protectedLayerCount = draft.SortingLayers.Count;
            draft.RemoveLayer(SortingLayer.IndexOf(SortingLayer.Default));
            Require(draft.SortingLayers.Count == protectedLayerCount,
                "A mandatory built-in Layer could be removed.");
            draft.AddLayer("Actors");
            draft.AddLayer("Effects");
            draft.MoveLayer(1, 1);
            draft.MoveLayer(7, -1);
            draft.RemoveLayer(6);
            Require(draft.IsDirty && draft.Error.Length == 0 &&
                    draft.Tags.SequenceEqual(["Untagged", "Hero", "Collectible"]) &&
                    draft.SortingLayers.Count == SortingLayer.BuiltInLayerCount + 1 &&
                    draft.SortingLayers[1].BuiltIn && draft.SortingLayers[1].BuiltInId == "Default" &&
                    draft.SortingLayers[5].Name == "Actors" && draft.SortingLayers[5].Value == 6 &&
                    draft.TagReplacements.TryGetValue("Player", out var renamed) && renamed == "Hero" &&
                    draft.TagReplacements.TryGetValue("Enemy", out var deleted) && deleted == "Untagged",
                $"Tag/Layer draft invalid: dirty={draft.IsDirty}, error='{draft.Error}', " +
                $"tags={string.Join(',', draft.Tags)}, layers=" +
                $"{string.Join(';', draft.SortingLayers.Select(layer => $"{layer.Value}:{layer.Name}:{layer.BuiltInId}"))}.");

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
            VerifyDocumentLayerRewrites();
            ProjectTagLayerSettingsApplier.Apply(draft);
            var persisted = Document.Load<ProjectSettingsDocument>(EditorProjectSettings.settingsPath);
            Require(persisted.Tags.SequenceEqual(["Untagged", "Hero", "Collectible"]) &&
                    TagManager.tags.SequenceEqual(persisted.Tags),
                "Applying the Tags draft did not update ProjectSettings.yaml and TagManager together.");
            Require(persisted.SortingLayers.Single(layer => layer.Value == SortingLayer.FromIndex(6)).Name ==
                    "Actors" && LayerMask.LayerToName(SortingLayer.FromIndex(6)) == "Actors",
                "Applying the Layers draft did not preserve its value or refresh SortingLayerRegistry.");
            Require(SortingLayer.Default == 2 && persisted.SortingLayers.Count(static layer => layer.BuiltIn) == 5,
                "Reordering a built-in Layer lost its stable identity or dynamic default index.");
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

    private static void VerifyDocumentLayerRewrites()
    {
        var scene = new SceneDocument
        {
            GameObjects =
            [
                new GameObjectDocument
                {
                    Name = "Layered",
                    Layer = 7,
                    Components =
                    [
                        new ComponentDocument
                        {
                            Type = typeof(Camera2D).FullName!,
                            Fields = new Dictionary<string, string>
                            {
                                ["sortingLayer"] = "7",
                                ["cullingMask"] = SortingLayer.ToMask(7).ToString(),
                                ["layer"] = "7"
                            }
                        }
                    ]
                }
            ]
        };
        var changed = ProjectTagLayerSettingsApplier.RewriteDocumentLayers(scene,
            new Dictionary<ulong, ulong> { [6] = 7, [7] = 6 });
        var component = scene.GameObjects[0].Components[0];
        Require(changed == 3 && scene.GameObjects[0].Layer == 6 &&
                component.Fields["sortingLayer"] == "6" &&
                component.Fields["cullingMask"] == SortingLayer.ToMask(6).ToString() &&
                component.Fields["layer"] == "7",
            "Scene Layer references and masks were not rewritten when Layers were reordered.");
    }

    private static List<SortingLayerDocument> CloneLayers(IEnumerable<SortingLayerDocument> layers) =>
        layers.Select(static layer => new SortingLayerDocument
        {
            Value = layer.Value,
            Name = layer.Name,
            BuiltIn = layer.BuiltIn,
            IsUi = layer.IsUi,
            BuiltInId = layer.BuiltInId
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
