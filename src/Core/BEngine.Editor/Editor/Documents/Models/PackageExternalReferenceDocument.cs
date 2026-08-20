using BEngine.Documents;

namespace BEngine.Editor.Documents;

public sealed class PackageExternalReferenceDocument : Document
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}
