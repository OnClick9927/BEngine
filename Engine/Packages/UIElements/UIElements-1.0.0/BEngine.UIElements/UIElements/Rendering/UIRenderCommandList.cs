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
    internal IReadOnlyList<UIElementLayout> HitRegions => _hitRegions;

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
        if (Layouts is UIElementLayoutSnapshot snapshot) return snapshot.TryGetRect(element, out rect);
        foreach (var layout in Layouts)
        {
            if (!ReferenceEquals(layout.Element, element)) continue;
            rect = layout.Rect;
            return true;
        }
        rect = default;
        return false;
    }

    /// <summary>Maximum vertical offset in logical pixels, using cached content bounds when available.</summary>
    public float GetMaximumScrollOffset(ScrollView scroll, Fix64? scale = null)
    {
        ArgumentNullException.ThrowIfNull(scroll);
        var resolvedScale = scale ?? Fix64.One;
        if (resolvedScale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        if (!TryGetRect(scroll, out var viewport)) return 0;
        if (Layouts is UIElementLayoutSnapshot snapshot && snapshot.TryGetContentExtent(scroll, out var extent))
            return Math.Max(0, (float)((extent - viewport.Height) / resolvedScale));
        var bottom = viewport.Y;
        foreach (var child in scroll.Children)
            if (TryGetRect(child, out var rect))
                bottom = Fix64.Max(bottom, rect.Y + rect.Height + (Fix64)child.style.marginBottom * resolvedScale +
                    (Fix64)scroll.scrollOffset * resolvedScale);
        return Math.Max(0, (float)((bottom - viewport.Y - viewport.Height) / resolvedScale));
    }

    public bool TryGetVisibleRect(VisualElement element, out UIElementRect rect)
    {
        ArgumentNullException.ThrowIfNull(element);
        foreach (var layout in _hitRegions)
        {
            if (!ReferenceEquals(layout.Element, element)) continue;
            rect = layout.Rect;
            return true;
        }
        rect = default;
        return false;
    }
}
