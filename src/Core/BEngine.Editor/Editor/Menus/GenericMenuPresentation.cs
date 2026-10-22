namespace BEngine.Editor;

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
