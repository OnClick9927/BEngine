namespace BEngine.Editor;

public sealed class BPackageExportOptions
{
    public string? Name { get; init; }
    public string PackageVersion { get; init; } = "1.0.0";
    public string Description { get; init; } = string.Empty;
    public string DefaultImportPath { get; init; } = string.Empty;
    public bool IncludeMetaFiles { get; init; } = true;
}
