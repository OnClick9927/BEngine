namespace BEngine.Editor;

// Core captures/restores structure and values; the workflow supplies preview-aware states and UI notifications.
internal sealed class FullHierarchyState : EditorHierarchySnapshot, IUndoState
{
    internal BObject[] Targets => CapturedTargets;

    private FullHierarchyState(GameObject root, FullHierarchyState? previous)
        : base(root, previous, static target => ObjectState.Capture(target))
    { }

    internal new static FullHierarchyState Capture(GameObject root) => new(root, null);
    public override FullHierarchyState CaptureInverse() => new(Root, this);
    IUndoState IUndoState.CaptureInverse() => CaptureInverse();

    public override void Restore()
    {
        base.Restore();
        EditorApplication.RaiseHierarchyChanged();
    }
}
