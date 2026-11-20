
namespace BEngine.Editor;

[RuntimeTypeAlias("BEngine.Editor.SceneView, BEngine.EditorWorkbench.Editor")]
[RuntimeTypeAlias("BEngine.Editor.SceneView, BEngine.EditorWorkbench.Editor, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null")]
public static class SceneView
{
    public static event Action<SceneViewToolContext>? duringSceneGui;
    public static event Action? toolInteractionCancelled;
    /// <summary>Dispatches registered tools inside a host Scene view GUI pass.</summary>
    public static bool DrawTools(Rect viewport, BEngine.Rendering.RenderCamera camera)
    {
        var context = new SceneViewToolContext(viewport, camera);
        if (duringSceneGui is { } handlers)
            foreach (Action<SceneViewToolContext> handler in Delegate.EnumerateInvocationList(handlers))
                EditorCallbacks.Invoke("SceneView.duringSceneGui", () => handler(context));
        return context.suppressDefaultTools;
    }
    /// <summary>Notifies tools that the host ended the current interaction.</summary>
    public static void CancelToolInteraction() =>
        EditorCallbacks.Dispatch(toolInteractionCancelled, nameof(toolInteractionCancelled));
    public static void FrameSelected() => EditorBridge.Host?.FrameSelected();
    public static void FrameLastActiveSceneView() => FrameSelected();
    public static void RepaintAll() => EditorBridge.Host?.RepaintAllWindows();
}
