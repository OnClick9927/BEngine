using BEngine.Editor.Documents;

namespace BEngine.Editor;

/// <summary>Supplies a workbench's initial layout and named, read-only presets.</summary>
public interface IEditorLayoutDefaults
{
    IReadOnlyList<string> Names { get; }
    EditorLayoutDocument CreateCurrent();
    EditorLayoutDocument CreatePreset(string name);
}
