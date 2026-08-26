using BEngine.Documents;

namespace BEngine.Editor.Documents;

public sealed class PackageDefinitionDocument : Document
{
    public string Format { get; set; } = "BEngine.Package";
    public int Version { get; set; } = 2;
    public string Id { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = "1.0.0";
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool EnabledByDefault { get; set; }
    public bool Required { get; set; }
    public PackageContentDocument Content { get; set; } = new();
    public PackageAssemblyDocument? Runtime { get; set; }
    public PackageAssemblyDocument? Editor { get; set; }

    public string? Assembly { get; set; }
    public string? Namespace { get; set; }
    public List<string>? Dependencies { get; set; }
}
