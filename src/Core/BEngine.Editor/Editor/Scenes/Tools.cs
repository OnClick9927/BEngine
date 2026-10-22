
namespace BEngine.Editor;

public static class Tools
{
    private static Tool _fallbackTool = Tool.Move;

    public static Tool current
    {
        get => EditorBridge.Host?.CurrentTool ?? _fallbackTool;
        set
        {
            _fallbackTool = value;
            if (EditorBridge.Host is { } host) host.CurrentTool = value;
        }
    }

    public static PivotMode pivotMode { get; set; } = PivotMode.Pivot;
    public static PivotRotation pivotRotation { get; set; } = PivotRotation.Global;
    public static bool hidden { get; set; }
    public static Vector2 handlePosition => Selection.activeGameObject is { } selected
        ? SceneHandleUtility.GetHandlePosition(selected)
        : Vector2.zero;
}
