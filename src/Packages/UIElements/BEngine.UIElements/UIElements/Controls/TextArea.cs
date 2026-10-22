namespace BEngine.UIElements;

public class TextArea : TextField
{
    public TextArea(string label = "") : base(label) { multiline = true; style.minHeight = 80; }
}
