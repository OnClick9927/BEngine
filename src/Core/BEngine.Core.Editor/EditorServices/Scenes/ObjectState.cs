namespace BEngine.Editor;

// The shared snapshot mechanics live in Core Editor. This adapter owns editor workflow policy.
internal sealed class ObjectState : EditorObjectSnapshot, IUndoState
{
    private ObjectState(BObject target) : base(target) { }
    public new static ObjectState Capture(BObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return EditorPreviewRegistry.CaptureOriginalState(target, () => new ObjectState(target));
    }
    IUndoState IUndoState.CaptureInverse() => Capture(Target);
    public override void Restore()
    {
        base.Restore();
        EditorUtility.SetDirty(Target);
    }
    public static void CopyValues(BObject source, BObject destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (source.GetType() != destination.GetType())
            throw new ArgumentException("Source and destination must have the same type.");
        Capture(source).CopyValuesTo(destination);
        EditorUtility.SetDirty(destination);
    }
}
