namespace BEngine.Editor;

internal sealed class BPackageImportPlan
{
    internal required string DestinationDirectory { get; init; }
    internal required IReadOnlyList<BPackageImportPlanEntry> Entries { get; init; }
    internal BPackageImportReceipt? PreviousReceipt { get; init; }
}
