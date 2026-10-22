namespace BEngine.UIElements;

public class HelpBox : Label
{
    public HelpBox(string text = "") : base(text)
    {
        style.paddingLeft = style.paddingRight = 10;
        style.paddingTop = style.paddingBottom = 8;
        style.backgroundColor = new UIColor(48, 65, 80);
        style.color = new UIColor(225, 237, 246);
        style.borderColor = new UIColor(80, 140, 185);
        style.borderWidth = 1;
    }
}
