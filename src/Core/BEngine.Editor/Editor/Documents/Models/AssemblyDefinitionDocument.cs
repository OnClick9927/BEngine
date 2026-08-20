using BEngine.Documents;

namespace BEngine.Editor.Documents;

public sealed class AssemblyDefinitionDocument : Document
{
    public string Format { get; set; } = "BEngine.AssemblyDefinition";
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Game";
    public string RootNamespace { get; set; } = "Game";
    public List<string> References { get; set; } = [];
    public List<string> IncludePlatforms { get; set; } = [];
    public List<string> ExcludePlatforms { get; set; } = [];
    public List<string> DefineConstraints { get; set; } = [];
    public bool AutoReferenced { get; set; } = true;
    public bool EditorOnly { get; set; }
    public bool AllowUnsafeCode { get; set; }
}
