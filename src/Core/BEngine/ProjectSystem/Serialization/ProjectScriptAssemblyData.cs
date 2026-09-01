namespace BEngine.ProjectSystem;

public sealed class ProjectScriptAssemblyData
{
    public string Assembly { get; set; } = string.Empty;
    public string BuildId { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public List<string> References { get; set; } = [];
}
