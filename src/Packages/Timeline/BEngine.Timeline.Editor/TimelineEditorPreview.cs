namespace BEngine.Editor;

internal sealed class TimelineEditorPreview(Action stopped) : IEditorPreview
{
    private PlayableDirector? _director;
    private bool _capturing;

    internal void Sample(PlayableDirector director, Fix64 time)
    {
        if (!EditorAssetWritePolicy.CanWrite) return;
        if (_director is not null && !ReferenceEquals(_director, director)) Dispose();
        _director = director; EditorPreviewRegistry.Register(this);
        try { director.time = time; director.Evaluate(); SceneView.RepaintAll(); }
        catch { Dispose(); throw; }
    }

    public T CaptureOriginalState<T>(BObject target, Func<T> capture)
    {
        if (_capturing || target is not (Component or GameObject) || _director is not { isDestroyed: false } director)
            return capture();
        var time = director.time; _capturing = true;
        try { director.Stop(); return capture(); }
        finally
        {
            try { director.time = time; director.Evaluate(); }
            finally { _capturing = false; }
        }
    }

    public void PrepareInspectorChanges(IReadOnlyCollection<UndoPropertyModification> modifications)
    {
        if (modifications.Count > 0) Dispose();
    }

    public void Dispose()
    {
        var director = _director; _director = null; EditorPreviewRegistry.Unregister(this);
        if (director is { isDestroyed: false }) director.Stop();
        stopped(); SceneView.RepaintAll();
    }
}
