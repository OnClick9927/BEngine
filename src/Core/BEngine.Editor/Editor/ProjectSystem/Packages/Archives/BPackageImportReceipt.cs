using BEngine.Documents;

namespace BEngine.Editor;

public sealed class BPackageImportReceipt : Document
{
    public string Format { get; set; } = "BEngine.BPackageImportReceipt";
    public int Version { get; set; } = 1;
    public string InstallationId { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public string DestinationDirectory { get; set; } = string.Empty;
    public string ArchiveSha256 { get; set; } = string.Empty;
    public List<BPackageImportReceiptEntry> Entries { get; set; } = [];
}
