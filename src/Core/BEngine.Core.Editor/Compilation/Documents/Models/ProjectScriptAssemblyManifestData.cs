namespace BEngine.ProjectSystem;

public sealed class ProjectScriptAssemblyManifestData
{
    public string Format { get; set; } = "BEngine.ProjectScriptAssemblies";
    public int Version { get; set; } = 1;
    public List<ProjectScriptAssemblyData> Assemblies { get; set; } = [];
}
