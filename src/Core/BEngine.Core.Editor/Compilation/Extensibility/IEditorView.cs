namespace BEngine.Editor;

/// <summary>Presentation-independent handle for a tool surface. Concrete UI systems own its contents.</summary>
public interface IEditorView
{
    string windowId { get; }
    void Show();
    void Close();
    void Focus();
    void Repaint();
}

public interface IEditorMenu
{
    int GetItemCount();
    void AddItem(string label, bool on, Action action);
    void AddDisabledItem(string label, bool on = false);
    void AddSeparator(string path);
}
