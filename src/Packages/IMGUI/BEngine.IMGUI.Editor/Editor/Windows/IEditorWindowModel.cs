namespace BEngine.Editor;

/// <summary>Read-only identity shared by a window's MVC objects.</summary>
public interface IEditorWindowModel
{
    string WindowId { get; }
}
