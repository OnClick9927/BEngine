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
    private static IEditorPreview[]? _snapshot;

    public static void Register(IEditorPreview preview)
    {
        if (Previews.Add(preview)) _snapshot = null;
    }
    public static void Unregister(IEditorPreview preview)
    {
        if (Previews.Remove(preview)) _snapshot = null;
    }

    // Each dispatch keeps its own immutable membership snapshot. A callback can
    // change registrations without altering an outer dispatch or retaining removals.
    private static IEditorPreview[] GetSnapshot() => _snapshot ??= Previews.ToArray();

    internal static void PrepareInspectorChanges(IReadOnlyCollection<UndoPropertyModification> modifications)
    {
        foreach (var preview in GetSnapshot()) preview.PrepareInspectorChanges(modifications);
    }

    internal static T CaptureOriginalState<T>(BObject target, Func<T> capture)
    {
        var previews = GetSnapshot();
        return previews.Length == 0 ? capture() : CaptureWithPreviews(previews, target, capture);
    }

    // Keep closure creation off the common path when no preview is active.
    private static T CaptureWithPreviews<T>(IEditorPreview[] previews, BObject target, Func<T> capture)
    {
        var chain = capture;
        foreach (var preview in previews)
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
        foreach (var preview in GetSnapshot()) recorded.UnionWith(preview.RecordInspectorChanges(changes));
        return recorded;
    }

    public static void StopAll()
    {
        foreach (var preview in GetSnapshot()) Stop(preview);
    }

    internal static void UnregisterAssembly(System.Reflection.Assembly assembly)
    {
        foreach (var preview in Previews.Where(preview => preview.GetType().Assembly == assembly).ToArray())
            Stop(preview);
    }

    private static void Stop(IEditorPreview preview)
    {
        try { EditorCallbacks.Invoke("Editor preview stop", preview.Dispose); }
        finally { Unregister(preview); }
    }
}
