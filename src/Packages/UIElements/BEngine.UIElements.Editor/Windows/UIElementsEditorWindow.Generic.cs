using BEngine.UIElements;
namespace BEngine.Editor;

public abstract class UIElementsEditorWindow<TModel, TController> : EditorWindow<TModel, TController>
    where TModel : class, IEditorWindowModel
    where TController : class, IEditorWindowController
{
    protected UIElementsEditorWindow() => content = new EditorWindowUIElementContainer(this);
    public EditorWindowUIElementContainer uiElementContainer => (EditorWindowUIElementContainer)content!;
    public VisualElement rootVisualElement => uiElementContainer.rootVisualElement;
}
