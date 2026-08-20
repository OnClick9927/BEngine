using BEngine.Documents;

namespace BEngine.Editor.Documents;

public sealed class PackageReferenceDocument : Document
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public bool Enabled { get; set; } = true;
}
