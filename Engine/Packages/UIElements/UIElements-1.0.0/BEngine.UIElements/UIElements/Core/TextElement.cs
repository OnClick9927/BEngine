using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class TextElement : VisualElement
{
    private string _text = string.Empty;
    public string text
    {
        get => _text;
        set { var text = value ?? string.Empty; if (_text == text) return; _text = text; MarkTextDirty(); }
    }
    protected TextElement(string text = "") => _text = text;
}
