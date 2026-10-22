namespace BEngine.Editor;

/// <summary>Default controller for common window commands.</summary>
public class EditorWindowController(
    EditorWindowMvcContext context,
    IEditorWindowModel model) : IEditorWindowController
{
    public string WindowId { get; } = context.WindowId;
    public IEditorWindowModel Model { get; } = model ?? throw new ArgumentNullException(nameof(model));
    public IEditorWindowView View { get; } = context.View ?? throw new ArgumentNullException(nameof(context));

    public virtual void Repaint() => View.Repaint();
    public virtual void Focus() => View.Focus();
    public virtual void Close() => View.Close();
}
