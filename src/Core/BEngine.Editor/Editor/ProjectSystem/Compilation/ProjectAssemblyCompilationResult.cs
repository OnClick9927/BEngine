using System.Reflection;

namespace BEngine.ProjectSystem.Editor;

internal sealed class ProjectAssemblyCompilationResult
{
    public ProjectAssemblyCompilationResult(
        IReadOnlyList<CompiledProjectAssembly> compiledAssemblies,
        IReadOnlyList<Assembly> loadedAssemblies)
    {
        CompiledAssemblies = compiledAssemblies;
        LoadedAssemblies = loadedAssemblies;
    }

    public IReadOnlyList<CompiledProjectAssembly> CompiledAssemblies { get; }
    public IReadOnlyList<Assembly> LoadedAssemblies { get; }
    public Assembly? EntryAssembly => LoadedAssemblies.LastOrDefault();
}
