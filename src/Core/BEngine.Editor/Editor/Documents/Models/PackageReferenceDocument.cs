using BEngine.Serialization;

namespace BEngine.Editor.Documents;

public sealed class PackageReferenceDocument
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Type { get; set; } = "package";
    public string ProjectDirectory { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
