namespace BEngine.ProjectSystem.Editor;

internal sealed class CompiledProjectAssembly
{
    public required ProjectAssemblyNode Node { get; init; }
    public required string BuildId { get; init; }
    public required string AssemblyPath { get; init; }
}
