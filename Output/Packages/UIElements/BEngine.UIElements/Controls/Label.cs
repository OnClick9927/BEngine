using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class Label : TextElement
{
    public event Action? doubleClicked;
    public Label(string text = "") : base(text) { }
    internal void RaiseDoubleClicked() => doubleClicked?.Invoke();
}
