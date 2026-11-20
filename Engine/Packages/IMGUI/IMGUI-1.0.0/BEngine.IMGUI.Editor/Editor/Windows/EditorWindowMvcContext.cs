namespace BEngine.Editor;

/// <summary>Context passed to per-window MVC factories.</summary>
public readonly record struct EditorWindowMvcContext(string WindowId, EditorWindow View);
