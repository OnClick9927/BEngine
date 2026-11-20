namespace BEngine.ProjectSystem;

/// <summary>Metadata for an extension authored inside one project.</summary>
public sealed class BPackageTemplateOptions
{
    public string Name { get; set; } = "NewPackage";
    public string Id { get; set; } = "com.project.newpackage";
    public string DisplayName { get; set; } = "NewPackage";
    public string RootNamespace { get; set; } = "Project.NewPackage";
    public string PackageVersion { get; set; } = "1.0.0";
    public string MinimumEngineVersion { get; set; } = EngineVersion.Current;
    public string Description { get; set; } = string.Empty;
}
