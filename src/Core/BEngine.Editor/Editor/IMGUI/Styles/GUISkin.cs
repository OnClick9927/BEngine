using BEngine.Rendering;

namespace BEngine.Editor;

public sealed class GUISkin
{
    private readonly Dictionary<string, GUIStyle> _custom = new(StringComparer.OrdinalIgnoreCase);
    public GUIStyle label { get; set; } = Style("label", C(0, 0, 0, 0));
    public GUIStyle box { get; set; } = Style("box", C(0.17f, 0.17f, 0.17f, 1));
    public GUIStyle button { get; set; } = Style("button", C(0.27f, 0.27f, 0.27f, 1));
    public GUIStyle toggle { get; set; } = Style("toggle", C(0, 0, 0, 0));
    public GUIStyle textField { get; set; } = Style("textField", C(0.16f, 0.16f, 0.16f, 1));
    public GUIStyle textArea { get; set; } = Style("textArea", C(0.16f, 0.16f, 0.16f, 1));
    public GUIStyle window { get; set; } = Style("window", C(0.20f, 0.20f, 0.20f, 1));
    public GUIStyle horizontalSlider { get; set; } = Style("horizontalSlider", C(0.12f, 0.12f, 0.12f, 1));
    public GUIStyle horizontalSliderThumb { get; set; } = Style("horizontalSliderThumb", C(0.28f, 0.55f, 0.78f, 1));
    public GUIStyle verticalSlider { get; set; } = Style("verticalSlider", C(0.12f, 0.12f, 0.12f, 1));
    public GUIStyle verticalSliderThumb { get; set; } = Style("verticalSliderThumb", C(0.28f, 0.55f, 0.78f, 1));
    public GUIStyle horizontalScrollbar { get; set; } = Style("horizontalScrollbar", C(0.12f, 0.12f, 0.12f, 1));
    public GUIStyle horizontalScrollbarThumb { get; set; } = Style("horizontalScrollbarThumb", C(0.36f, 0.36f, 0.36f, 1));
    public GUIStyle verticalScrollbar { get; set; } = Style("verticalScrollbar", C(0.12f, 0.12f, 0.12f, 1));
    public GUIStyle verticalScrollbarThumb { get; set; } = Style("verticalScrollbarThumb", C(0.36f, 0.36f, 0.36f, 1));

    public GUIStyle? FindStyle(string styleName) => GetStyle(styleName, false);
    public GUIStyle GetStyle(string styleName) => GetStyle(styleName, true)!;
    public void AddStyle(GUIStyle style) => _custom[style.name] = style;
    private GUIStyle? GetStyle(string styleName, bool throwIfMissing)
    {
        var builtIn = styleName.ToLowerInvariant() switch
        {
            "label" => label, "box" => box, "button" => button, "toggle" => toggle,
            "textfield" => textField, "textarea" => textArea, "window" => window,
            "horizontalslider" => horizontalSlider, "horizontalsliderthumb" => horizontalSliderThumb,
            "verticalslider" => verticalSlider, "verticalsliderthumb" => verticalSliderThumb,
            "horizontalscrollbar" => horizontalScrollbar,
            "horizontalscrollbarthumb" => horizontalScrollbarThumb,
            "verticalscrollbar" => verticalScrollbar,
            "verticalscrollbarthumb" => verticalScrollbarThumb,
            _ => _custom.GetValueOrDefault(styleName)
        };
        return builtIn ?? (throwIfMissing
            ? throw new ArgumentException($"GUIStyle '{styleName}' was not found.", nameof(styleName)) : null);
    }
    private static GUIStyle Style(string name, Color background)
    {
        var style = new GUIStyle(name); style.normal.backgroundColor = background; return style;
    }
    private static Color C(float r, float g, float b, float a) => new((Fix64)r, (Fix64)g, (Fix64)b, (Fix64)a);
}
