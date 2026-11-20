namespace BEngine.Editor;

/// <summary>The view contract implemented by every <see cref="EditorWindow"/>.</summary>
public interface IEditorWindowView
{
    string windowId { get; }
    bool isOpen { get; }
    void Repaint();
    void Focus();
    void Close();
}
