namespace BEngine.UIElements;

internal sealed class UIElementDocument
{
    public string Type { get; set; } = nameof(VisualElement);
    public string Name { get; set; } = string.Empty;
    public string Tooltip { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public ulong SortingLayer { get; set; } = BEngine.SortingLayer.Ui;
    public int OrderInLayer { get; set; }
    public string MaterialShader { get; set; } = string.Empty;
    public string Atlas { get; set; } = string.Empty;
    public List<string> Classes { get; set; } = [];
    public UIStyleDocument Style { get; set; } = new();
    public List<UIElementDocument> Children { get; set; } = [];
}
