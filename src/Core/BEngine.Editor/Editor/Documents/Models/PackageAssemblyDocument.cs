using BEngine.Documents;

namespace BEngine.Editor.Documents;

public sealed class PackageAssemblyDocument
{
    public string Assembly { get; set; } = string.Empty;
    public string RootNamespace { get; set; } = string.Empty;
    public List<PackageDependencyDocument> Dependencies { get; set; } = [];
    public List<PackageExternalReferenceDocument> PackageReferences { get; set; } = [];
}
