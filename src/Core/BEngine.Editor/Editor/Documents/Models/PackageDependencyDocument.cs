using BEngine.Documents;

namespace BEngine.Editor.Documents;

public sealed class PackageDependencyDocument : Document
{
    public string PackageId { get; set; } = string.Empty;
    public string Target { get; set; } = "runtime";
    public bool Optional { get; set; }
}
