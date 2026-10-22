using BEngine.Editor;

namespace BEngine.UIElements.Editor;

internal static class UIElementsSettingsProvider
{
    [SettingsProvider]
    private static SettingsProvider CreatePreferences() => new("Preferences/UIElement",
        SettingsScope.User, ["UIElements", "UXML", "USS", "HTML", "GPU"])
    {
        label = "UIElements",
        guiHandler = _ =>
        {
            GUILayout.Label("UIElements", EditorStyles.largeLabel);
            GUILayout.Label("Retained-mode GPU UI, UXML/USS and HTML conversion.");
            GUILayout.Space(8);
            if (GUILayout.Button("Open UI Builder", GUILayout.Width(180))) UIBuilderWindow.Open();
        }
    };

    [SettingsProvider]
    private static SettingsProvider CreateProjectSettings() => new("Project/UIElement",
        SettingsScope.Project, ["UIElements", "UXML", "USS", "runtime UI"])
    {
        label = "UIElements",
        guiHandler = _ =>
        {
            GUILayout.Label("UIElements Project Settings", EditorStyles.largeLabel);
            GUILayout.Label("Runtime UI resources are built into the engine Resources folder.");
        }
    };
}
