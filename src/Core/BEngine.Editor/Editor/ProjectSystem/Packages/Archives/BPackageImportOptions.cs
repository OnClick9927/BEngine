namespace BEngine.Editor;

public sealed class BPackageImportOptions
{
    public BPackageConflictPolicy ConflictPolicy { get; init; } = BPackageConflictPolicy.Fail;
    public string DestinationDirectory { get; init; } = string.Empty;
    public string InstallationId { get; init; } = string.Empty;
    public BPackageImportMode Mode { get; init; } = BPackageImportMode.Import;
    public BPackageModifiedFilePolicy ModifiedFilePolicy { get; init; } =
        BPackageModifiedFilePolicy.Preserve;
    public bool VerifyHashes { get; init; } = true;
    public int MaximumEntryCount { get; init; } = 100_000;
    public long MaximumFileSize { get; init; } = 512L * 1024 * 1024;
    public long MaximumTotalSize { get; init; } = 2L * 1024 * 1024 * 1024;
}
