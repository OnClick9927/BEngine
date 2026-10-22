using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public sealed class ToolbarSpacer : VisualElement
{
    public ToolbarSpacer() => style.flexGrow = 1;
}
