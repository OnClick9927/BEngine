namespace BEngine.UIElements;

internal sealed class UIAssetDocument
{
    public string Format { get; set; } = "BEngine.UI";
    public int Version { get; set; } = 1;
    public UIElementDocument Root { get; set; } = new();
}
