using BEngine.Rendering;

namespace BEngine.Editor;

public sealed class GUIStyle
{
    public string name { get; set; } = string.Empty;
    public GUIStyleState normal { get; } = new();
    public GUIStyleState hover { get; } = new() { backgroundColor = C(0.29f, 0.29f, 0.29f, 1) };
    public GUIStyleState active { get; } = new() { backgroundColor = C(0.18f, 0.38f, 0.56f, 1) };
    public GUIStyleState focused { get; } = new() { backgroundColor = C(0.20f, 0.36f, 0.50f, 1) };
    public GUIStyleState disabled { get; } = new()
    {
        textColor = C(0.41f, 0.41f, 0.41f, 1),
        backgroundColor = C(0.20f, 0.20f, 0.20f, 1)
    };
    public Fix64 fixedWidth { get; set; }
    public Fix64 fixedHeight { get; set; }
    public Fix64 borderWidth { get; set; }
    public Fix64 fontSize { get; set; } = 13;
    public TextAnchor alignment { get; set; } = TextAnchor.MiddleLeft;
    public bool wordWrap { get; set; }
    public bool richText { get; set; }
    public bool stretchWidth { get; set; } = true;
    public bool stretchHeight { get; set; }

    public GUIStyle() { }
    public GUIStyle(string name) => this.name = name;
    public GUIStyle(GUIStyle other)
    {
        ArgumentNullException.ThrowIfNull(other);
        name = other.name; fixedWidth = other.fixedWidth; fixedHeight = other.fixedHeight;
        borderWidth = other.borderWidth;
        fontSize = other.fontSize; alignment = other.alignment; wordWrap = other.wordWrap;
        richText = other.richText; stretchWidth = other.stretchWidth; stretchHeight = other.stretchHeight;
        normal.textColor = other.normal.textColor; normal.backgroundColor = other.normal.backgroundColor;
        hover.textColor = other.hover.textColor; hover.backgroundColor = other.hover.backgroundColor;
        active.textColor = other.active.textColor; active.backgroundColor = other.active.backgroundColor;
        focused.textColor = other.focused.textColor; focused.backgroundColor = other.focused.backgroundColor;
        disabled.textColor = other.disabled.textColor; disabled.backgroundColor = other.disabled.backgroundColor;
        normal.borderColor = other.normal.borderColor; hover.borderColor = other.hover.borderColor;
        active.borderColor = other.active.borderColor; focused.borderColor = other.focused.borderColor;
        disabled.borderColor = other.disabled.borderColor;
    }

    private static Color C(float r, float g, float b, float a) => new((Fix64)r, (Fix64)g, (Fix64)b, (Fix64)a);

    public Vector2 CalcSize(GUIContent content)
    {
        var text = content?.text ?? string.Empty;
        return new Vector2(GUITextMetrics.MeasureWidth(text, fontSize, GUIUtility.fontFamily),
            fixedHeight > 0 ? fixedHeight : GUITextMetrics.MeasureLineHeight(fontSize, GUIUtility.fontFamily));
    }
}
