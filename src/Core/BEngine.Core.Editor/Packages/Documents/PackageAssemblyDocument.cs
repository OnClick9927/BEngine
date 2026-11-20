namespace BEngine.Editor.Documents;

public sealed class PackageAssemblyDocument
{
    public string Assembly { get; set; } = string.Empty;
    public string RootNamespace { get; set; } = string.Empty;
    /// <summary>Loaded from the installation before source compilation; updated with the engine installation.</summary>
    public bool Bootstrap { get; set; }
    public List<string> Aliases { get; set; } = [];
    public List<string> HostReferences { get; set; } = [];
    public List<PackageDependencyDocument> Dependencies { get; set; } = [];
    public List<PackageExternalReferenceDocument> PackageReferences { get; set; } = [];
}
