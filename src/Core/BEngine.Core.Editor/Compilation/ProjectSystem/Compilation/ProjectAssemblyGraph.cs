namespace BEngine.ProjectSystem.Editor;

internal sealed class ProjectAssemblyGraph
{
    public ProjectAssemblyGraph(IReadOnlyList<ProjectAssemblyNode> orderedAssemblies) =>
        OrderedAssemblies = orderedAssemblies;

    public IReadOnlyList<ProjectAssemblyNode> OrderedAssemblies { get; }

    public IReadOnlyList<ProjectAssemblyNode> RuntimeAssemblies =>
        OrderedAssemblies.Where(assembly => !assembly.EditorOnly).ToArray();

    public IReadOnlyList<ProjectAssemblyNode> EditorAssemblies =>
        OrderedAssemblies.Where(assembly => assembly.EditorOnly).ToArray();
}
