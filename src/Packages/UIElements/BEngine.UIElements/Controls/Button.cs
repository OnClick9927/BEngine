using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class Button : TextElement
{
    public event Action? clicked;
    public Button(Action? clickEvent = null, string text = "") : base(text)
    {
        if (clickEvent is not null) clicked += clickEvent;
    }
    internal void Click() => clicked?.Invoke();
}
