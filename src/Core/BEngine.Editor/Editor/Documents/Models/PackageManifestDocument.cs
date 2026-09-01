using BEngine.Documents;

namespace BEngine.Editor.Documents;

public sealed class PackageManifestDocument
{
    public string Format { get; set; } = "BEngine.Packages";
    public int Version { get; set; } = 1;
    public List<PackageReferenceDocument> Packages { get; set; } = [];
}
