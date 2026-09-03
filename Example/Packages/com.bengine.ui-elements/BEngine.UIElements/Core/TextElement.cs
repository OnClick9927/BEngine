using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class TextElement : VisualElement
{
    private string _text = string.Empty;
    public string text { get => _text; set => Set(ref _text, value ?? string.Empty); }
    protected TextElement(string text = "") => _text = text;
}
