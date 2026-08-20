
namespace BEngine.Editor;

public static class SceneView
{
    public static void FrameSelected() => EditorBridge.Host?.FrameSelected();
    public static void FrameLastActiveSceneView() => FrameSelected();
    public static void RepaintAll() => EditorBridge.Host?.RepaintAllWindows();
}
