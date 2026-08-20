namespace BEngine.Editor;

internal sealed class FloatingEditorWindow
{
    internal FloatingEditorWindow(EditorWindow window, EditorWindowState state, Rect bounds)
    {
        Window = window;
        State = state;
        Bounds = bounds;
    }

    public EditorWindow Window { get; }
    public EditorWindowState State { get; internal set; }
    public Rect Bounds { get; internal set; }
    internal Rect InteractionStartBounds { get; set; }
    internal Vector2 InteractionStartPointer { get; set; }
    internal int ResizeEdges { get; set; }
    internal bool IsDragging { get; set; }
    internal bool IsResizing { get; set; }
    internal bool DockPressed { get; set; }
    internal bool MenuPressed { get; set; }
    internal bool ClosePressed { get; set; }
}
