using System.Reflection;

namespace BEngine.Editor;

public abstract class EditorBuiltinBrowserWindow : EditorWindow<EditorBuiltinBrowserModel, EditorBuiltinBrowserController>
{
    private protected readonly EditorScrollView _scroll = new();
    protected override EditorBuiltinBrowserModel CreateWindowModel(EditorWindowMvcContext context) => new(context.WindowId);
    protected override EditorBuiltinBrowserController CreateWindowController(EditorWindowMvcContext context,
        EditorBuiltinBrowserModel windowModel) => new(context, windowModel);

    protected void DrawSearch(string placeholder)
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        model.Search = EditorToolbar.SearchField(model.Search, placeholder, GUILayout.ExpandWidth(true));
        GUILayout.EndHorizontal();
    }
}
