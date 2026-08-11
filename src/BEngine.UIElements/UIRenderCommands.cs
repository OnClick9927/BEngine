namespace BEngine.UIElements;

public enum UIRenderCommandType
{
    SolidRect,
    Text,
    Image
}

public readonly record struct UIRenderCommand(
    UIRenderCommandType Type,
    VisualElement Element,
    UIElementRect Rect,
    UIElementRect ClipRect,
    UIColor Color,
    string Content = "",
    float FontSize = 0);

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
}

public static class UIRenderListBuilder
{
    public static UIRenderCommandList Build(
        VisualElement root,
        int viewportWidth,
        int viewportHeight,
        Fix64? scale = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        viewportWidth = Math.Max(1, viewportWidth);
        viewportHeight = Math.Max(1, viewportHeight);
        var layouts = UIElementLayoutEngine.Calculate(root, viewportWidth, viewportHeight, scale);
        var layoutByElement = layouts.ToDictionary(layout => layout.Element, layout => layout.Rect);
        var commands = new List<UIRenderCommand>(layouts.Count * 2);
        var hitRegions = new List<UIElementLayout>(layouts.Count);
        var viewport = new UIElementRect(0, 0, viewportWidth, viewportHeight);
        BuildElement(root, viewport, layoutByElement, commands, hitRegions);
        return new UIRenderCommandList(layouts, commands, hitRegions);
    }

    private static void BuildElement(
        VisualElement element,
        UIElementRect inheritedClip,
        IReadOnlyDictionary<VisualElement, UIElementRect> layouts,
        ICollection<UIRenderCommand> commands,
        ICollection<UIElementLayout> hitRegions)
    {
        if (!layouts.TryGetValue(element, out var rect)) return;
        var elementClip = Intersect(inheritedClip, rect);
        if (HasArea(elementClip)) hitRegions.Add(new UIElementLayout(element, elementClip));
        var background = ResolveBackground(element);
        if (background.A > 0 && HasArea(elementClip))
            commands.Add(new UIRenderCommand(
                UIRenderCommandType.SolidRect, element, rect, inheritedClip, background));

        if (element is Image image && !string.IsNullOrWhiteSpace(image.sourcePath) && HasArea(elementClip))
            commands.Add(new UIRenderCommand(
                UIRenderCommandType.Image, element, rect, elementClip, UIColor.FromRgb(255, 255, 255),
                image.sourcePath));

        var text = ResolveText(element);
        if (!string.IsNullOrWhiteSpace(text) && HasArea(elementClip))
            commands.Add(new UIRenderCommand(
                UIRenderCommandType.Text, element, rect, elementClip, ResolveTextColor(element), text,
                element.style.fontSize > 0 ? element.style.fontSize : 14));

        var childClip = element.style.overflow == Overflow.Visible ? inheritedClip : elementClip;
        foreach (var child in element.Children) BuildElement(child, childClip, layouts, commands, hitRegions);
    }

    private static UIColor ResolveBackground(VisualElement element) =>
        element.style.backgroundColor ?? element switch
        {
            Button => new UIColor(42, 126, 112),
            TextField or FloatField or IntegerField or DropdownField => new UIColor(38, 41, 45),
            _ => UIColor.Clear
        };

    private static UIColor ResolveTextColor(VisualElement element) =>
        element.style.color ?? new UIColor(235, 238, 242);

    private static string ResolveText(VisualElement element) => element switch
    {
        TextField field => field.value,
        FloatField field => field.value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        IntegerField field => field.value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Toggle toggle => $"{(toggle.value ? "[x]" : "[ ]")} {toggle.label}",
        DropdownField dropdown => dropdown.value,
        TextElement text => text.text,
        _ => string.Empty
    };

    private static UIElementRect Intersect(UIElementRect first, UIElementRect second)
    {
        var left = Fix64.Max(first.X, second.X);
        var top = Fix64.Max(first.Y, second.Y);
        var right = Fix64.Min(first.X + first.Width, second.X + second.Width);
        var bottom = Fix64.Min(first.Y + first.Height, second.Y + second.Height);
        return new UIElementRect(left, top, Fix64.Max(0, right - left), Fix64.Max(0, bottom - top));
    }

    private static bool HasArea(UIElementRect rect) => rect.Width > 0 && rect.Height > 0;
}
