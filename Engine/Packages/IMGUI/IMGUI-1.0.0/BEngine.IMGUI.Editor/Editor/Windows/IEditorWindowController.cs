namespace BEngine.Editor;

/// <summary>Controller contract exposed through <see cref="EditorWindowRegistry"/>.</summary>
public interface IEditorWindowController
{
    string WindowId { get; }
    IEditorWindowModel Model { get; }
    IEditorWindowView View { get; }
    void Repaint();
    void Focus();
    void Close();
}
