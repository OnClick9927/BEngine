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
    public Fix64? Left { get; set; }
    public Fix64? Top { get; set; }
    public Fix64? Right { get; set; }
    public Fix64? Bottom { get; set; }
    public Fix64? Width { get; set; }
    public Fix64? Height { get; set; }
    public Fix64? MinWidth { get; set; }
    public Fix64? MinHeight { get; set; }
    public Fix64? MaxWidth { get; set; }
    public Fix64? MaxHeight { get; set; }
    public Fix64? FlexGrow { get; set; }
    public Fix64? FlexShrink { get; set; }
    public Fix64? MarginLeft { get; set; }
    public Fix64? MarginTop { get; set; }
    public Fix64? MarginRight { get; set; }
    public Fix64? MarginBottom { get; set; }
    public Fix64? PaddingLeft { get; set; }
    public Fix64? PaddingTop { get; set; }
    public Fix64? PaddingRight { get; set; }
    public Fix64? PaddingBottom { get; set; }
    public Fix64? FontSize { get; set; }
    public Fix64? BorderWidth { get; set; }
    public string Color { get; set; } = string.Empty;
    public string BackgroundColor { get; set; } = string.Empty;
    public string BorderColor { get; set; } = string.Empty;
}
