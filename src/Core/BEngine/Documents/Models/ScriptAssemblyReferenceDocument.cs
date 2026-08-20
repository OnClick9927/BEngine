namespace BEngine.Documents;

public sealed class ScriptAssemblyReferenceDocument : Document
{
    public string Format { get; set; } = "BEngine.ScriptAssemblyReference";
    public int Version { get; set; } = 1;
    public string Assembly { get; set; } = string.Empty;
    public string BuildId { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
}
