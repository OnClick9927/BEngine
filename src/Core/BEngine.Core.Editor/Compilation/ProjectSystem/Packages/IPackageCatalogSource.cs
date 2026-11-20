using BEngine.Editor.Documents;

namespace BEngine.ProjectSystem;

/// <summary>Supplies a repository snapshot without searching the repository in the editor.</summary>
public interface IPackageCatalogSource
{
    PackageCatalogSnapshot GetSnapshot(string packagesRoot, string engineVersion);
}

public sealed class PackageCatalogEntry
{
    public string Path { get; set; } = string.Empty;
    public PackageDefinitionDocument Document { get; set; } = new();
    public PackageDistributionKind Distribution { get; set; }
}

/// <summary>Whether this exact release belongs to the selected engine's distribution.</summary>
public enum PackageDistributionKind
{
    Unknown,
    Bundled,
    External
}

public sealed class PackageCatalogSnapshot
{
    public string PackagesRoot { get; set; } = string.Empty;
    public string EngineVersion { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public List<PackageCatalogEntry> Packages { get; set; } = [];
}

// Shared with the standalone repository host; no engine or package assemblies are loaded there.
public sealed class PackageCatalogRequest
{
    public int ProtocolVersion { get; set; } = 1;
    public string EngineVersion { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
}

public sealed class PackageCatalogResponse
{
    public int ProtocolVersion { get; set; } = 1;
    public string Error { get; set; } = string.Empty;
    public bool NotModified { get; set; }
    public PackageCatalogSnapshot? Snapshot { get; set; }
}
