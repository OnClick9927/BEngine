using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public sealed class ToolbarMenu : ToolbarButton
{
    public event Action<ContextMenuBuilder>? menuRequested;

    public ToolbarMenu(string text = "") : base(text: text) { }

    internal ContextMenuBuilder BuildMenu()
    {
        var menu = new ContextMenuBuilder();
        menuRequested?.Invoke(menu);
        return menu;
    }
}
