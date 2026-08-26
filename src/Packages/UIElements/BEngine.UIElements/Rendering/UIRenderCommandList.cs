namespace BEngine.UIElements;

public sealed class UIRenderCommandList
{
    private readonly IReadOnlyList<UIElementLayout> _hitRegions;

    internal UIRenderCommandList(
        IReadOnlyList<UIElementLayout> layouts,
        IReadOnlyList<UIRenderCommand> commands,
        IReadOnlyList<UIElementLayout> hitRegions)
    {
        Layouts = layouts;
        Commands = commands;
        _hitRegions = hitRegions;
    }

    public IReadOnlyList<UIElementLayout> Layouts { get; }
    public IReadOnlyList<UIRenderCommand> Commands { get; }

    public VisualElement? Pick(Vector2 position)
    {
        for (var index = _hitRegions.Count - 1; index >= 0; index--)
        {
            var layout = _hitRegions[index];
            if (layout.Element.enabledInHierarchy && layout.Rect.Contains(position)) return layout.Element;
        }
        return null;
    }

    public bool TryGetRect(VisualElement element, out UIElementRect rect)
    {
        ArgumentNullException.ThrowIfNull(element);
        foreach (var layout in Layouts)
        {
            if (!ReferenceEquals(layout.Element, element)) continue;
            rect = layout.Rect;
            return true;
        }
        rect = default;
        return false;
    }
}
