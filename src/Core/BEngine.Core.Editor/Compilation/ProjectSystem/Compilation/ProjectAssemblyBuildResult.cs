namespace BEngine.ProjectSystem.Editor;

internal sealed class ProjectAssemblyBuildResult
{
    public ProjectAssemblyBuildResult(
        IReadOnlyList<CompiledProjectAssembly> compiledAssemblies,
        IReadOnlyDictionary<string, string> baseReferences,
        IReadOnlyDictionary<string, string> existingProjectReferences)
    {
        CompiledAssemblies = compiledAssemblies;
        BaseReferences = baseReferences;
        ExistingProjectReferences = existingProjectReferences;
    }

    public IReadOnlyList<CompiledProjectAssembly> CompiledAssemblies { get; }
    public IReadOnlyDictionary<string, string> BaseReferences { get; }
    public IReadOnlyDictionary<string, string> ExistingProjectReferences { get; }
}
