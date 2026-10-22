using BEngine.UIElements;
namespace BEngine.Editor;

public abstract class UIElementsEditorWindow : EditorWindow
{
    protected UIElementsEditorWindow() => content = new EditorWindowUIElementContainer(this);
    public EditorWindowUIElementContainer uiElementContainer => (EditorWindowUIElementContainer)content!;
    public VisualElement rootVisualElement => uiElementContainer.rootVisualElement;
}
