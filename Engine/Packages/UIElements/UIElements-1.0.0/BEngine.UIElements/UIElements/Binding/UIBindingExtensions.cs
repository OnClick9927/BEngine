namespace BEngine.UIElements;

public static class UIBindingExtensions
{
    public static UIBinding Bind(this VisualElement root, object source)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(source);
        return new UIBinding(root, source);
    }
}
