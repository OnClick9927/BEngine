namespace BEngine.Editor;

/// <summary>Routes document commands to the focused extension window.</summary>
public interface IEditorDocumentCommandHandler
{
    bool TryGetDocumentCommand(MainMenuCommand command, out Action? execute);
}
