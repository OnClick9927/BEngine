namespace BEngine.Workbench;

/// <summary>Application implementation of the distribution's editor entry point.</summary>
public sealed class EditorApplicationEntry : IEditorApplicationEntry
{
    public int Run(string[] arguments) => Program.Main(arguments);
}
