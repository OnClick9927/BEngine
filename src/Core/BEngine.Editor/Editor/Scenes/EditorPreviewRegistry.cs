namespace BEngine.Editor;

/// <summary>A reversible preview supplied by an optional editor extension.</summary>
public interface IEditorPreview : IDisposable
{
    void PrepareInspectorChanges(IReadOnlyCollection<UndoPropertyModification> modifications) { }
    T CaptureOriginalState<T>(BObject target, Func<T> capture) => capture();
    IReadOnlySet<BObject> RecordInspectorChanges(IReadOnlyCollection<UndoPropertyModification> modifications) =>
        new HashSet<BObject>();
}

/// <summary>Coordinates previews with scene saving, Inspector edits and Undo.</summary>
public static class EditorPreviewRegistry
{
    private static readonly HashSet<IEditorPreview> Previews = [];

    public static void Register(IEditorPreview preview) => Previews.Add(preview);
    public static void Unregister(IEditorPreview preview) => Previews.Remove(preview);

    internal static void PrepareInspectorChanges(IReadOnlyCollection<UndoPropertyModification> modifications)
    {
        foreach (var preview in Previews.ToArray()) preview.PrepareInspectorChanges(modifications);
    }

    internal static T CaptureOriginalState<T>(BObject target, Func<T> capture)
    {
        var chain = capture;
        foreach (var preview in Previews.ToArray())
        {
            var next = chain;
            chain = () => preview.CaptureOriginalState(target, next);
        }
        return chain();
    }

    internal static HashSet<BObject> RecordInspectorChanges(IEnumerable<UndoPropertyModification> modifications)
    {
        var changes = modifications.ToArray();
        var recorded = new HashSet<BObject>();
        foreach (var preview in Previews.ToArray()) recorded.UnionWith(preview.RecordInspectorChanges(changes));
        return recorded;
    }

    public static void StopAll()
    {
        foreach (var preview in Previews.ToArray()) Stop(preview);
    }

    internal static void UnregisterAssembly(System.Reflection.Assembly assembly)
    {
        foreach (var preview in Previews.Where(preview => preview.GetType().Assembly == assembly).ToArray())
            Stop(preview);
    }

    private static void Stop(IEditorPreview preview)
    {
        try { EditorFeatureGuard.Invoke("Editor preview stop", preview.Dispose); }
        finally { Previews.Remove(preview); }
    }
}
