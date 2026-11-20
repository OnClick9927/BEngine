namespace BEngine.Editor;

// Public properties are captured by Core. Dimensions require the resource owner's Resize
// operation, so undo/copy must not assign backing fields or retain a native target snapshot.
internal sealed class RenderTextureSnapshotAdapter : EditorObjectSnapshotAdapter
{
    public override Type ObjectType => typeof(RenderTexture);
    public override object Capture(BObject target)
    {
        var texture = (RenderTexture)target;
        return (texture.width, texture.height);
    }

    public override void Restore(BObject target, object? payload)
    {
        var (width, height) = ((int, int))payload!;
        ((RenderTexture)target).Resize(width, height);
    }
}
