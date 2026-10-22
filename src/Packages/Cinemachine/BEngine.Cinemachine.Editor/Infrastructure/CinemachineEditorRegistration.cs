using BEngine.Editor;

namespace BEngine.Cinemachine.Editor;

internal static class CinemachineEditorRegistration
{
    [InitializeOnLoadMethod]
    private static void RegisterIcons()
    {
        EditorIconRegistry.Register(typeof(CinemachineBrain), "Cinemachine.png");
        EditorIconRegistry.Register(typeof(CinemachineCamera), "Cinemachine.png");
    }
}
