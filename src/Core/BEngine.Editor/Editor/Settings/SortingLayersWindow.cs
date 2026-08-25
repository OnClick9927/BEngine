namespace BEngine.Editor;

[EditorWindowIcon("Icons/Windows/ProjectSettings.png")]
public sealed class SortingLayersWindow : EditorWindow
{
    [MenuItem("Edit/Sorting Layers...", false, 905)]
    [MenuItem("Window/Rendering/Sorting Layers", false, 210)]
    public static void Open() => TagLayerSettingsProvider.OpenLayers();

    protected override void OnEnable()
    {
        saveToLayout = false;
        EditorApplication.delayCall += RedirectLegacyWindow;
    }

    private void RedirectLegacyWindow()
    {
        TagLayerSettingsProvider.OpenLayers();
        Close();
    }
}
