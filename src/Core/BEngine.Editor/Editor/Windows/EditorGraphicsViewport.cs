namespace BEngine.Editor;

public readonly record struct EditorGraphicsViewport(
    Rect LocalRect,
    int TargetWidth,
    int TargetHeight)
{
    public bool IsValid => LocalRect.width > 0 && LocalRect.height > 0 &&
                           TargetWidth > 0 && TargetHeight > 0;
}
