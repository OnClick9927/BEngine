namespace BEngine.UIElements;

internal sealed class UIElementDocument
{
    public string Type { get; set; } = nameof(VisualElement);
    public string CustomUxml { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tooltip { get; set; } = string.Empty;
    public AccessibilityRole AccessibilityRole { get; set; }
    public string AccessibilityName { get; set; } = string.Empty;
    public string AccessibilityDescription { get; set; } = string.Empty;
    public string AccessibilityValue { get; set; } = string.Empty;
    public bool AccessibilityHidden { get; set; }
    public AccessibilityLiveRegion AccessibilityLiveRegion { get; set; }
    public bool Focusable { get; set; }
    public int TabIndex { get; set; } = -1;
    public string Text { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int TextMaxLength { get; set; } = -1;
    public string TextPlaceholder { get; set; } = string.Empty;
    public bool TextMultiline { get; set; }
    public bool TextPassword { get; set; }
    public bool TextReadOnly { get; set; }
    public bool TextScrollToEnd { get; set; }
    public char TextMaskCharacter { get; set; } = '*';
    public ulong SortingLayer { get; set; } = BEngine.SortingLayer.Ui;
    public int OrderInLayer { get; set; }
    public string MaterialShader { get; set; } = string.Empty;
    public string Atlas { get; set; } = string.Empty;
    public List<string> Classes { get; set; } = [];
    public UIStyleDocument Style { get; set; } = new();
    public List<UIElementDocument> Children { get; set; } = [];
}
