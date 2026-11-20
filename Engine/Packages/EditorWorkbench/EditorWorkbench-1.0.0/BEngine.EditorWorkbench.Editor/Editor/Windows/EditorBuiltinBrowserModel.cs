using System.Reflection;

namespace BEngine.Editor;

public sealed class EditorBuiltinBrowserModel(string windowId) : EditorWindowModel(windowId)
{
    public string Search { get; set; } = string.Empty;
    public Vector2 Scroll { get; set; }
    public string Selected { get; set; } = string.Empty;
}
