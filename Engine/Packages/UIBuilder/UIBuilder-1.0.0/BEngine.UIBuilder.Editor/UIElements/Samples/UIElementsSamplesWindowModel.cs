using BEngine.Editor;

namespace BEngine.UIElements.Editor;

public sealed class UIElementsSamplesWindowModel(string windowId) : EditorWindowModel(windowId)
{
    public int SelectedCategory { get; internal set; }
    public Vector2 ContentScroll { get; set; }
    public bool ToggleValue { get; set; } = true;
    public int RadioValue { get; set; }
    public float SliderValue { get; set; } = 0.42f;
    public int IntValue { get; set; } = 12;
    public float FloatValue { get; set; } = 3.5f;
    public string TextValue { get; set; } = "Editable text";
    public string MultilineValue { get; set; } = "A multiline field\nfor longer values.";
    public int ChoiceValue { get; set; } = 1;
    public int ListSelection { get; set; }
    public int ButtonClicks { get; set; }
    public Vector2 Vector2Value { get; set; } = new(12, 24);
    public Vector4 Vector4Value { get; set; } = new(1, 2, 3, 4);
    public Color ColorValue { get; set; } = new((Fix64)0.18, (Fix64)0.55, (Fix64)0.85, Fix64.One);
    public BObject? ObjectValue { get; set; }
}
