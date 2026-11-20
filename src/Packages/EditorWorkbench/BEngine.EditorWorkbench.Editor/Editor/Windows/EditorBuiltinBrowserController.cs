using System.Reflection;

namespace BEngine.Editor;

public sealed class EditorBuiltinBrowserController(EditorWindowMvcContext context, EditorBuiltinBrowserModel model)
    : EditorWindowController<EditorBuiltinBrowserWindow, EditorBuiltinBrowserModel>(context, model);
