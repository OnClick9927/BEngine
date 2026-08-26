using BEngine.Rendering;

namespace BEngine.Editor;

public sealed class GUIStyle
{
    [field: SerializeField]
    public string name { get; set; } = string.Empty;
    [field: SerializeField]
    public GUIStyleState normal { get; set; } = new();
    [field: SerializeField]
    public GUIStyleState hover { get; set; } = new() { backgroundColor = C(0.29f, 0.29f, 0.29f, 1) };
    [field: SerializeField]
    public GUIStyleState active { get; set; } = new() { backgroundColor = C(0.18f, 0.38f, 0.56f, 1) };
    [field: SerializeField]
    public GUIStyleState focused { get; set; } = new() { backgroundColor = C(0.20f, 0.36f, 0.50f, 1) };
    [field: SerializeField]
    public GUIStyleState onNormal { get; set; } = new();
    [field: SerializeField]
    public GUIStyleState onHover { get; set; } = new() { backgroundColor = C(0.29f, 0.29f, 0.29f, 1) };
    [field: SerializeField]
    public GUIStyleState onActive { get; set; } = new() { backgroundColor = C(0.18f, 0.38f, 0.56f, 1) };
    [field: SerializeField]
    public GUIStyleState onFocused { get; set; } = new() { backgroundColor = C(0.20f, 0.36f, 0.50f, 1) };
    [field: SerializeField]
    public GUIStyleState disabled { get; set; } = new()
    {
        textColor = C(0.41f, 0.41f, 0.41f, 1),
        backgroundColor = C(0.20f, 0.20f, 0.20f, 1)
    };
    [field: SerializeField]
    public Fix64 fixedWidth { get; set; }
    [field: SerializeField]
    public Fix64 fixedHeight { get; set; }
    [field: SerializeField]
    public Fix64 borderWidth { get; set; }
    [field: SerializeField]
    public Fix64 fontSize { get; set; } = 14;
    [field: SerializeField]
    public TextAnchor alignment { get; set; } = TextAnchor.MiddleLeft;
    [field: SerializeField]
    public bool wordWrap { get; set; }
    [field: SerializeField]
    public bool richText { get; set; }
    [field: SerializeField]
    public bool stretchWidth { get; set; } = true;
    [field: SerializeField]
    public bool stretchHeight { get; set; }

    public GUIStyle() { }
    public GUIStyle(string name) => this.name = name;
    public GUIStyle(GUIStyle other) => CopyFrom(other);

    public void CopyFrom(GUIStyle other)
    {
        ArgumentNullException.ThrowIfNull(other);
        name = other.name; fixedWidth = other.fixedWidth; fixedHeight = other.fixedHeight;
        borderWidth = other.borderWidth;
        fontSize = other.fontSize; alignment = other.alignment; wordWrap = other.wordWrap;
        richText = other.richText; stretchWidth = other.stretchWidth; stretchHeight = other.stretchHeight;
        normal.CopyFrom(other.normal);
        hover.CopyFrom(other.hover);
        active.CopyFrom(other.active);
        focused.CopyFrom(other.focused);
        onNormal.CopyFrom(other.onNormal);
        onHover.CopyFrom(other.onHover);
        onActive.CopyFrom(other.onActive);
        onFocused.CopyFrom(other.onFocused);
        disabled.CopyFrom(other.disabled);
    }

    public GUIStyle Clone() => new(this);

    private static Color C(float r, float g, float b, float a) => new((Fix64)r, (Fix64)g, (Fix64)b, (Fix64)a);

    public Vector2 CalcSize(GUIContent content)
    {
        var text = content?.text ?? string.Empty;
        return new Vector2(GUITextMetrics.MeasureWidth(text, fontSize, GUIUtility.fontFamily),
            fixedHeight > 0 ? fixedHeight : GUITextMetrics.MeasureLineHeight(fontSize, GUIUtility.fontFamily));
    }
}
