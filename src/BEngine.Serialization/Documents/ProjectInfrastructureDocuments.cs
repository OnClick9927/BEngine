namespace BEngine.Serialization.Documents;

public sealed class ProjectSettingsDocument
{
    public string Format { get; set; } = "BEngine.ProjectSettings";
    public int Version { get; set; } = 1;
    public string Locale { get; set; } = "zh-CN";
    public string CompanyName { get; set; } = "DefaultCompany";
    public string ProductName { get; set; } = "BEngine Game";
    public int DefaultScreenWidth { get; set; } = 1280;
    public int DefaultScreenHeight { get; set; } = 720;
    public bool FullScreen { get; set; }
}

public sealed class PackageManifestDocument
{
    public string Format { get; set; } = "BEngine.Packages";
    public int Version { get; set; } = 1;
    public List<PackageReferenceDocument> Packages { get; set; } = [];
}

public sealed class PackageReferenceDocument
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public bool Enabled { get; set; } = true;
}

public sealed class PackageDefinitionDocument
{
    public string Format { get; set; } = "BEngine.Package";
    public int Version { get; set; } = 2;
    public string Id { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = "1.0.0";
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool EnabledByDefault { get; set; } = true;
    public bool Required { get; set; }
    public PackageAssemblyDocument? Runtime { get; set; }
    public PackageAssemblyDocument? Editor { get; set; }

    // BEngine.Package v1 compatibility. The package loader normalizes these fields into Runtime.
    public string? Assembly { get; set; }
    public string? Namespace { get; set; }
    public List<string>? Dependencies { get; set; }
}

public sealed class PackageAssemblyDocument
{
    public string Assembly { get; set; } = string.Empty;
    public string RootNamespace { get; set; } = string.Empty;
    public List<PackageDependencyDocument> Dependencies { get; set; } = [];
}

public sealed class PackageDependencyDocument
{
    public string PackageId { get; set; } = string.Empty;
    public string Target { get; set; } = "runtime";
    public bool Optional { get; set; }
}

public sealed class AssemblyDefinitionDocument
{
    public string Format { get; set; } = "BEngine.AssemblyDefinition";
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Game";
    public string RootNamespace { get; set; } = "Game";
    public List<string> References { get; set; } = [];
    public List<string> IncludePlatforms { get; set; } = [];
    public List<string> ExcludePlatforms { get; set; } = [];
    public bool AutoReferenced { get; set; } = true;
    public bool EditorOnly { get; set; }
}
