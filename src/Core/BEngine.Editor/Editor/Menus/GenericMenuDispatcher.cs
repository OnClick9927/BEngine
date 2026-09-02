namespace BEngine.Editor;

internal static class GenericMenuDispatcher
{
    [ThreadStatic] private static GenericMenuPresentation _currentPresentation;

    internal static Action<IReadOnlyList<GenericMenuItem>>? Handler { get; set; }

    internal static GenericMenuPresentation CurrentPresentation => _currentPresentation;

    internal static void Show(IReadOnlyList<GenericMenuItem> items) =>
        Show(items, GenericMenuPresentation.Context);

    internal static void Show(
        IReadOnlyList<GenericMenuItem> items,
        GenericMenuPresentation presentation)
    {
        if (Handler is not { } handler) return;
        var previous = _currentPresentation;
        _currentPresentation = presentation;
        try
        {
            EditorFeatureGuard.Invoke("GenericMenuDispatcher.Show", () => handler(items));
        }
        finally
        {
            _currentPresentation = previous;
        }
    }
}
