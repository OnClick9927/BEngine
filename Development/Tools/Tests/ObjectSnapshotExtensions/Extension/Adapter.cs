using BEngine;
using BEngine.Editor;

public sealed class ExternalSnapshotAdapter : EditorObjectSnapshotAdapter
{
    public override Type ObjectType => typeof(BObject);
    public override object Capture(BObject target) => new int[] { 1, 2, 3 };
    public override void Restore(BObject target, object? payload) { }
}
