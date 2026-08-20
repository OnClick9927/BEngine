namespace BEngine.Editor;

internal sealed class BPackageImportPlanEntry
{
    internal required string RelativePath { get; init; }
    internal string? SourceRelativePath { get; init; }
    internal required bool IsDirectory { get; init; }
    internal required BPackageImportAction Action { get; set; }
    internal string? ExpectedSha256 { get; init; }
    internal BPackageImportReceiptEntry? PreviousReceiptEntry { get; init; }
}
