using BEngine.Editor;

namespace BEngine.UIElements.Editor;

public sealed class UIElementsSamplesWindowController :
    EditorWindowController<UIElementsSamplesWindow, UIElementsSamplesWindowModel>
{
    public UIElementsSamplesWindowController(
        EditorWindowMvcContext context,
        UIElementsSamplesWindowModel model) : base(context, model) { }

    public void SelectCategory(int index)
    {
        Model.SelectedCategory = index;
        Model.ContentScroll = Vector2.zero;
        Repaint();
    }
}
