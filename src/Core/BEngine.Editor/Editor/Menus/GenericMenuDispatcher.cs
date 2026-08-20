namespace BEngine.Editor;

internal static class GenericMenuDispatcher
{
    internal static Action<IReadOnlyList<GenericMenuItem>>? Handler { get; set; }
    internal static void Show(IReadOnlyList<GenericMenuItem> items)
    {
        if (Handler is { } handler)
            EditorFeatureGuard.Invoke("GenericMenuDispatcher.Show", () => handler(items));
    }
}
