using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem.Editor;

internal sealed class ProjectAssemblyNode
{
    public required string Name { get; init; }
    public required string RootNamespace { get; init; }
    public required string DefinitionPath { get; init; }
    public required IReadOnlyList<string> SourcePaths { get; init; }
    public required IReadOnlyList<string> References { get; init; }
    public required bool EditorOnly { get; init; }
    public required bool AutoReferenced { get; init; }
    public required bool AllowUnsafeCode { get; init; }
    public AssemblyDefinitionDocument? Definition { get; init; }
}
