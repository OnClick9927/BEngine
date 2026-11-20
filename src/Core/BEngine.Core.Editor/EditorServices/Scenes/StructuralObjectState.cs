namespace BEngine.Editor;

// Core owns structural identity; this adapter owns editor workflow policy.
internal sealed class StructuralObjectState : EditorObjectStructureSnapshot
{
    private StructuralObjectState(BObject target) : base(target) { }
    internal new static StructuralObjectState Capture(BObject target) => new(target);

    internal bool TryRemove()
    {
        var removed = TryRemove(out var affectedScene);
        if (removed && affectedScene is not null) EditorSceneManager.MarkSceneDirty(affectedScene);
        return removed;
    }

    internal void Remove()
    {
        if (!TryRemove())
            throw new InvalidOperationException($"{Target.GetType().Name} could not be removed.");
    }

    public override void Restore()
    {
        base.Restore();
        EditorUtility.SetDirty(Target);
        EditorApplication.RaiseHierarchyChanged();
    }
}
