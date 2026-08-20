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
            Require(projectProviders.Any(item => item.settingsPath == "Project/Player"),
                "Built-in Player project provider was not discovered.");
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

            var renderMarkers = SettingsWindowRenderRegressionTests.Run(testDirectory, projectPath);
            Console.WriteLine("EDITOR_SETTINGS_WINDOWS_OK|reflection,scopes,yaml,appearance,scale,locale," +
                              $"font-gpu,project,{string.Join(',', renderMarkers)}");
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
