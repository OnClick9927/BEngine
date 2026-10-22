namespace BEngine.UIElements;

internal sealed class UIStyleDocument
{
    public Dictionary<string, string> CssDeclarations { get; set; } = [];
    public FlexDirection? FlexDirection { get; set; }
    public DisplayStyle? Display { get; set; }
    public Align? AlignItems { get; set; }
    public Justify? JustifyContent { get; set; }
    public Overflow? Overflow { get; set; }
    public Position? Position { get; set; }
    public float? Left { get; set; }
    public float? Top { get; set; }
    public float? Right { get; set; }
    public float? Bottom { get; set; }
    public float? Width { get; set; }
    public float? Height { get; set; }
    public float? MinWidth { get; set; }
    public float? MinHeight { get; set; }
    public float? MaxWidth { get; set; }
    public float? MaxHeight { get; set; }
    public float? FlexGrow { get; set; }
    public float? FlexShrink { get; set; }
    public float? MarginLeft { get; set; }
    public float? MarginTop { get; set; }
    public float? MarginRight { get; set; }
    public float? MarginBottom { get; set; }
    public float? PaddingLeft { get; set; }
    public float? PaddingTop { get; set; }
    public float? PaddingRight { get; set; }
    public float? PaddingBottom { get; set; }
    public float? FontSize { get; set; }
    public float? BorderWidth { get; set; }
    public string Color { get; set; } = string.Empty;
    public string BackgroundColor { get; set; } = string.Empty;
    public string BorderColor { get; set; } = string.Empty;
}
