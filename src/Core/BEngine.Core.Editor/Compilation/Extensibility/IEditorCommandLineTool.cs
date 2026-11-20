namespace BEngine.Editor;

public interface IEditorCommandLineTool
{
    string Option { get; }
    int Run(IReadOnlyList<string> arguments);
}
