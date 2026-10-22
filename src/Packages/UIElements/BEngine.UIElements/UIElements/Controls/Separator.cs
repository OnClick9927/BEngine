namespace BEngine.UIElements;

public class Separator : VisualElement
{
    public Separator()
    {
        style.height = style.minHeight = 1;
        style.flexShrink = 0;
        style.marginTop = style.marginBottom = 4;
        style.backgroundColor = new UIColor(100, 105, 112);
    }
}
