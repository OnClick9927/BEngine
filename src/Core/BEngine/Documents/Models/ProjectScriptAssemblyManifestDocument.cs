namespace BEngine.Documents;

public sealed class ProjectScriptAssemblyManifestDocument : Document
{
    public string Format { get; set; } = "BEngine.ProjectScriptAssemblies";
    public int Version { get; set; } = 1;
    public List<ProjectScriptAssemblyDocument> Assemblies { get; set; } = [];
}
