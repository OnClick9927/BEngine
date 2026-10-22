namespace BEngine.UIElements.Editor;

public sealed record UIBuilderLibraryItem(
    string Category,
    string DisplayName,
    string Tooltip,
    string IconPath,
    Func<VisualElement> Factory)
{
    public VisualElement Create()
    {
        var element = Factory();
        if (string.IsNullOrWhiteSpace(element.name)) element.name = DisplayName.Replace(" ", string.Empty);
        return element;
    }
}
