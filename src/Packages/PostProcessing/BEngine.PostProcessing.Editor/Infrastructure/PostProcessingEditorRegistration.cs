using BEngine.Editor;

namespace BEngine.PostProcessing.Editor;

internal static class PostProcessingEditorRegistration
{
    [InitializeOnLoadMethod]
    private static void RegisterIcons()
    {
        EditorIconRegistry.Register(typeof(PostProcessLayer), "PostProcessing.png");
        EditorIconRegistry.Register(typeof(PostProcessVolume), "PostProcessing.png");
    }
}
