namespace BEngine.Editor;

internal enum GenericMenuPresentationKind
{
    Context,
    DropDown,
    AdvancedDropDown
}

internal readonly record struct GenericMenuPresentation(
    GenericMenuPresentationKind Kind,
    bool HasAnchor,
    Rect Anchor)
{
    internal bool IsAdvanced => Kind == GenericMenuPresentationKind.AdvancedDropDown;

    internal static GenericMenuPresentation Context =>
        new(GenericMenuPresentationKind.Context, false, default);

    internal static GenericMenuPresentation DropDown(Rect anchor) =>
        new(GenericMenuPresentationKind.DropDown, true, anchor);

    internal static GenericMenuPresentation AdvancedDropDown() =>
        new(GenericMenuPresentationKind.AdvancedDropDown, false, default);

    internal static GenericMenuPresentation AdvancedDropDown(Rect anchor) =>
        new(GenericMenuPresentationKind.AdvancedDropDown, true, anchor);
}

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
