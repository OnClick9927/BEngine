namespace BEngine.Editor.Documents;

public sealed class PackageDependencyDocument
{
    public string PackageId { get; set; } = string.Empty;
    public string Target { get; set; } = "runtime";
    /// <summary>Optional Git source used by the Hub to obtain a missing required package.</summary>
    public string? GitUrl { get; set; }
    public bool Optional { get; set; }
}
