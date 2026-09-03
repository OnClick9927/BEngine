using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public class ToolbarButton : Button
{
    public ToolbarButton(Action? clickEvent = null, string text = "") : base(clickEvent, text) { }
}
