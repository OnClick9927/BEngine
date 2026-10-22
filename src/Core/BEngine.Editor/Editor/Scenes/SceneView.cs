
namespace BEngine.Editor;

public static class SceneView
{
    public static event Action<SceneViewToolContext>? duringSceneGui;
    public static event Action? toolInteractionCancelled;
    internal static bool DrawTools(Rect viewport, BEngine.Rendering.RenderCamera camera)
    {
        var context = new SceneViewToolContext(viewport, camera);
        if (duringSceneGui is { } handlers)
            foreach (Action<SceneViewToolContext> handler in Delegate.EnumerateInvocationList(handlers))
                EditorFeatureGuard.Invoke("SceneView.duringSceneGui", () => handler(context));
        return context.suppressDefaultTools;
    }
    internal static void CancelToolInteraction() =>
        EditorCallbackDispatcher.Invoke(toolInteractionCancelled, nameof(toolInteractionCancelled));
    public static void FrameSelected() => EditorBridge.Host?.FrameSelected();
    public static void FrameLastActiveSceneView() => FrameSelected();
    public static void RepaintAll() => EditorBridge.Host?.RepaintAllWindows();
}
