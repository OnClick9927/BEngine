using System.Globalization;
using BEngine.Serialization;
using BEngine.Documents;

namespace BEngine.UIElements;

public sealed class UIStyleDocument : Document
{
    public FlexDirection FlexDirection { get; set; }
    public DisplayStyle Display { get; set; }
    public Align AlignItems { get; set; } = Align.Stretch;
    public Justify JustifyContent { get; set; }
    public Overflow Overflow { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float MinWidth { get; set; }
    public float MinHeight { get; set; }
    public float FlexGrow { get; set; }
    public float FlexShrink { get; set; } = 1;
    public float MarginLeft { get; set; }
    public float MarginTop { get; set; }
    public float MarginRight { get; set; }
    public float MarginBottom { get; set; }
    public float PaddingLeft { get; set; }
    public float PaddingTop { get; set; }
    public float PaddingRight { get; set; }
    public float PaddingBottom { get; set; }
    public float FontSize { get; set; }
    public float BorderWidth { get; set; }
    public string Color { get; set; } = string.Empty;
    public string BackgroundColor { get; set; } = string.Empty;
    public string BorderColor { get; set; } = string.Empty;
}
