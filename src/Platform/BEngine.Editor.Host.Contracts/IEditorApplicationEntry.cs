namespace BEngine.Editor;

/// <summary>A distribution-supplied editor application invoked by the generic launcher.</summary>
public interface IEditorApplicationEntry
{
    int Run(string[] arguments);
}
